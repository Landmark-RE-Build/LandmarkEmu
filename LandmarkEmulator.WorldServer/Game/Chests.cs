using LandmarkEmulator.WorldServer.Storage;
using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.Shared.GameTable.Text;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using LandmarkEmulator.WorldServer.Network.Message.Model.Shared;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Storage chests: placed chest props hold items. Protocol (ChestBase 0xFF, u32 sub after the opcode; client
    /// dispatcher FUN_142dad120, see docs/protocol.md "Chests"):
    /// - clicking a chest (Command InteractRequest) → server EnableWindow (1) {u64 chest} → the client records the
    ///   chest and asks InventoryListRequest (3) {u64 chest} → server InventoryListResponse (4), which opens the chest
    ///   window (Lua DisplayChestWindow);
    /// - items are moved with InventoryBase TransferItem; chest locations have owner type 3 and the chest as owner;
    /// - the server then sends ChestAddItem (5) / ChestDeleteItem (6) to every player looking into that chest.
    /// The chest's inventory guid (which keys the client's chest view) is the chest prop's guid.
    /// </summary>
    public static class Chests
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>"You can place up to 50 chests on a claim" (update notes, 21 March 2016).</summary>
        public const int MaxPerSite = 50;

        public const uint EnableWindowSub = 1, StopObservingSub = 2, ListRequestSub = 3, ListResponseSub = 4,
            AddItemSub = 5, DeleteItemSub = 6, UpdateItemSub = 7;
        /// <summary>Owner type of an item location inside a chest (TransferItem; the client's item OwnerType).</summary>
        public const uint ChestOwnerType = 3;
        /// <summary>ListResponse results: 1 opens the window; 4 shows Chest.ClaimAccessDenied.</summary>
        private const uint ResultOk = 1, ResultAccessDenied = 4;
        /// <summary>"Open the Simple Medium Chest prop by clicking on it" (Props To You).</summary>
        private const uint OpenChestObjective = 740;
        /// <summary>PropDefs id of the Simple Medium Chest.</summary>
        private const uint SimpleMediumChest = 2113;

        private static readonly object sync = new();
        /// <summary>Chest guid → its items.</summary>
        private static readonly Dictionary<ulong, List<ChestItemModel>> contents = new();
        /// <summary>Who has which chest open (for live updates).</summary>
        private static readonly Dictionary<WorldSession, ulong> watching = new();
        /// <summary>Chest prop ids → slots (built from PropDefs).</summary>
        private static Dictionary<uint, uint> chestProps;

        /// <summary>Loads every chest's contents (server start).</summary>
        public static void Load()
        {
            List<ChestItemModel> stored = DatabaseManager.Instance.CharacterDatabase.GetChestItems();
            lock (sync)
                foreach (ChestItemModel item in stored)
                    Of(item.ChestGuid).Add(item);
            log.Info($"Loaded {stored.Count} chest item(s) in {contents.Count} chest(s).");
        }

        private static List<ChestItemModel> Of(ulong chest)
        {
            if (!contents.TryGetValue(chest, out var list))
                contents[chest] = list = new List<ChestItemModel>();
            return list;
        }

        /// <summary>
        /// Player storage chests: the craftable props named "Chest …" or "… Treasure Chest" (PropDefs with a prop recipe).
        /// The client sizes the window from the server's ListResponse; sizes follow its Chests.txt steps (10-35): small
        /// 10, large 30, the rest 20.
        /// </summary>
        public static uint Slots(uint propId)
        {
            if (chestProps == null)
            {
                var found = new Dictionary<uint, uint>();
                foreach (var def in GameTableManager.Instance.PropDefs.Entries)
                {
                    if (def.PropRecipeId == 0)
                        continue;
                    string name = TextManager.Instance.GetTextForId(def.NameStringId) ?? "";
                    if (!name.Contains("Chest", StringComparison.OrdinalIgnoreCase))
                        continue;
                    found[def.Id] = name.Contains("Small", StringComparison.OrdinalIgnoreCase) ? 10u
                        : name.Contains("Large", StringComparison.OrdinalIgnoreCase) ? 30u : 20u;
                }
                chestProps = found;
                log.Info($"{found.Count} chest props.");
            }
            return chestProps.TryGetValue(propId, out uint slots) ? slots : 0;
        }

        public static bool IsChest(ulong guid) => Props.Find(guid) is { } prop && Slots(prop.PropId) > 0;

        /// <summary>Can this player use the chest: its owner, or anyone the build site under it lets plunder.</summary>
        private static bool MayUse(WorldSession session, Props.PlacedProp prop)
        {
            if (prop.OwnerGuid == session.PlayerGuid)
                return true;
            var p = Terrain.TerrainEdits.ToGlobal(new System.Numerics.Vector3(prop.Spawn.Position.X, prop.Spawn.Position.Y,
                prop.Spawn.Position.Z));
            return Claims.IsInsideAny(prop.Zone, p.X, p.Y, p.Z)
                && Claims.Allows(session.PlayerGuid, p.X, p.Y, p.Z, Claims.PermissionPlunder, prop.Zone);
        }

        /// <summary>A click on a chest (Command InteractRequest): EnableWindow makes the client ask for the contents.</summary>
        public static void Interact(WorldSession session, ulong chest)
        {
            session.EnqueueMessage(ZoneMessageOpcode.ChestBase, Build(EnableWindowSub, w => w.Write(chest)));
        }

        /// <summary>ChestBase from the client: u32 sub, then its body.</summary>
        public static void Handle(WorldSession session, uint sub, byte[] body)
        {
            switch (sub)
            {
                case ListRequestSub when body.Length >= 8:
                    SendContents(session, BitConverter.ToUInt64(body, 0));
                    break;
                case StopObservingSub:
                    lock (sync)
                        watching.Remove(session);
                    break;
                default:
                    log.Debug($"Chest packet {sub} from {session.CharacterName}: {BitConverter.ToString(body)}");
                    break;
            }
        }

        /// <summary>
        /// InventoryListResponse (4, reader FUN_142da90f0): u32 result, u64 owner, u64 chest, u64 inventory, u32 title
        /// text id, u32 slots, bool unsolicited, u32 length + item blob (u32 n, per item: bool definition follows (false),
        /// item record).
        /// </summary>
        private static void SendContents(WorldSession session, ulong chest)
        {
            Props.PlacedProp prop = Props.Find(chest);
            uint slots = prop == null ? 0 : Slots(prop.PropId);
            bool allowed = slots > 0 && prop.Zone == session.ZoneId && MayUse(session, prop);
            List<ChestItemModel> items;
            lock (sync)
            {
                items = allowed ? Of(chest).ToList() : new();
                if (allowed)
                    watching[session] = chest;
            }
            var blob = new List<byte>();
            var bw = new GamePacketWriter(blob);
            bw.Write((uint)items.Count);
            foreach (ChestItemModel item in items)
            {
                bw.Write(false);
                ToPacket(item).WriteRecord(bw);   // {bool, record} per item, no trailing byte (FUN_142dab870)
            }
            session.EnqueueMessage(ZoneMessageOpcode.ChestBase, Build(ListResponseSub, w =>
            {
                w.Write(allowed ? ResultOk : ResultAccessDenied);
                w.Write(prop?.OwnerGuid ?? 0ul);
                w.Write(chest);
                w.Write(chest);                                 // inventory guid
                w.Write(prop?.Spawn.NameId ?? 0u);              // window title
                w.Write(slots);
                w.Write(false);
                w.Write((uint)blob.Count);
                w.WriteBytes(blob.ToArray());
            }));
            if (allowed && prop.PropId == SimpleMediumChest)
                Achievements.Progress(session, OpenChestObjective);
            log.Info($"{session.CharacterName} opened chest 0x{chest:X} ({items.Count}/{slots}){(allowed ? "" : ": refused")}.");
        }

        /// <summary>A chest item as the client's item record: owner type 3, the chest as inventory and chest owner.</summary>
        private static InventoryItem ToPacket(ChestItemModel item)
        {
            var packet = new InventoryItem
            {
                Guid = item.Id,
                Count = item.Count,
                ContainerId = 0,
                SlotId = item.Slot,
                OwnerType = ChestOwnerType,
                InventoryId = item.ChestGuid,
                ChestOwnerGuid = item.ChestGuid,
                ChestSlot = item.Slot,
                SecondsSinceLastCast = int.MaxValue
            };
            Inventory.Inventory.FillDefinition(packet, item.ItemDefinitionId);
            return packet;
        }

        /// <summary>
        /// TransferItem touching a chest (either location with owner type 3). Bag → chest moves (part of) a stack into a
        /// free chest slot; chest → bag puts it back in the bag (stacking as usual); chest → chest moves it to an
        /// empty slot. Returns whether anything moved.
        /// </summary>
        /// <summary>TransferItemResponse results (client names, FUN_143b966c0).</summary>
        public const uint TransferDone = 0x1, DestinationFull = 0x2, SourceNotFound = 0x3, TransferFailedInInventory = 0x13,
            NoSourceChestPermission = 0x16, NoDestinationChestPermission = 0x17, NoTradeIntoClaimChest = 0x20,
            ChestWithItemsIntoChest = 0x25;

        public static uint Transfer(WorldSession session, TransferItemRequest request)
        {
            bool toChest = request.Destination.OwnerType == ChestOwnerType;
            bool fromChest = request.Source.OwnerType == ChestOwnerType;
            ulong chest = toChest ? request.Destination.OwnerGuid : request.Source.OwnerGuid;
            Props.PlacedProp prop = Props.Find(chest);
            uint slots = prop == null ? 0 : Slots(prop.PropId);
            if (slots == 0 || prop.Zone != session.ZoneId || !MayUse(session, prop))
            {
                log.Info($"{session.CharacterName}: chest transfer refused (chest 0x{chest:X}, slots {slots}).");
                return slots == 0 || prop.Zone != session.ZoneId ? SourceNotFound : toChest ? NoDestinationChestPermission : NoSourceChestPermission;
            }

            if (toChest && !fromChest)
                return Deposit(session, chest, slots, request);
            if (fromChest && !toChest)
                return Withdraw(session, chest, request);
            return MoveWithin(session, chest, slots, request);
        }

        private static uint Deposit(WorldSession session, ulong chest, uint slots, TransferItemRequest request)
        {
            CharacterItemModel item = session.Inventory.Get(request.ItemGuid);
            if (item == null || item.Container == Inventory.Inventory.EquippedContainer)
                return SourceNotFound;
            if (HasItems(item.Id))
                return ChestWithItemsIntoChest;
            // "Can't place NO_TRADE item into claim's chest if you're not claim owner" (client reason 0x20).
            if (Inventory.ItemDefinitionManager.Get(item.ItemDefinitionId) is { NoTrade: true }
                && Props.Find(chest) is Props.PlacedProp placed && !OwnsSiteOf(session, placed))
                return NoTradeIntoClaimChest;
            uint count = request.Quantity == 0 ? item.Count : Math.Min(request.Quantity, item.Count);
            ChestItemModel stored;
            lock (sync)
            {
                List<ChestItemModel> list = Of(chest);
                uint? slot = request.Destination.Slot < slots && list.All(i => i.Slot != request.Destination.Slot)
                    ? request.Destination.Slot
                    : Enumerable.Range(0, (int)slots).Select(s => (uint?)s).FirstOrDefault(s => list.All(i => i.Slot != s));
                if (slot == null)
                    return DestinationFull;
                stored = new ChestItemModel
                {
                    // The whole stack keeps its guid; a split part gets a new one.
                    Id = count == item.Count ? item.Id : Inventory.Inventory.NewItemGuid(),
                    ChestGuid = chest,
                    ItemDefinitionId = item.ItemDefinitionId,
                    Count = count,
                    Slot = slot.Value
                };
                list.Add(stored);
            }
            session.TakeFromStack(item.Id, count);
            ChestItemModel copy = stored;
            DatabaseManager.Instance.CharacterDatabase.Save(c => c.ChestItem.Add(copy))
                .ContinueWith(t => log.Error(t.Exception, "Failed to save chest item."),
                    System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            BroadcastAdd(chest, stored);
            return TransferDone;
        }

        /// <summary>Does the player own the build site the chest stands on (or is it on no site)?</summary>
        private static bool OwnsSiteOf(WorldSession session, Props.PlacedProp chest)
        {
            var at = new System.Numerics.Vector3(chest.Spawn.Position.X, chest.Spawn.Position.Y, chest.Spawn.Position.Z);
            return Claims.ClaimAt(chest.Zone, at) is not ulong site || Claims.OwnerOf(site) == session.PlayerGuid;
        }

        private static uint Withdraw(WorldSession session, ulong chest, TransferItemRequest request)
        {
            ChestItemModel item;
            lock (sync)
                item = Of(chest).FirstOrDefault(i => i.Id == request.ItemGuid)
                    ?? Of(chest).FirstOrDefault(i => i.Slot == request.Source.Slot);
            if (item == null)
                return SourceNotFound;
            uint count = request.Quantity == 0 ? item.Count : Math.Min(request.Quantity, item.Count);
            session.GiveItem(item.ItemDefinitionId, count);
            bool gone;
            lock (sync)
            {
                item.Count -= count;
                gone = item.Count == 0;
                if (gone)
                    Of(chest).Remove(item);
            }
            ulong id = item.Id;
            uint left = item.Count;
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                var model = new ChestItemModel { Id = id };
                c.ChestItem.Attach(model);
                if (gone)
                    c.ChestItem.Remove(model);
                else
                    model.Count = left;
            }).ContinueWith(t => log.Error(t.Exception, "Failed to save chest item."),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            BroadcastDelete(chest, id);
            if (!gone)
                BroadcastAdd(chest, item);
            return TransferDone;
        }

        private static uint MoveWithin(WorldSession session, ulong chest, uint slots, TransferItemRequest request)
        {
            ChestItemModel item;
            lock (sync)
            {
                List<ChestItemModel> list = Of(chest);
                item = list.FirstOrDefault(i => i.Id == request.ItemGuid);
                if (item == null)
                    return SourceNotFound;
                if (request.Destination.Slot >= slots || list.Any(i => i.Slot == request.Destination.Slot))
                    return TransferFailedInInventory;
                item.Slot = request.Destination.Slot;
            }
            ulong id = item.Id;
            uint slot = item.Slot;
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                var model = new ChestItemModel { Id = id };
                c.ChestItem.Attach(model);
                model.Slot = slot;
                c.MarkModified(model, nameof(model.Slot));
            });
            BroadcastDelete(chest, id);
            BroadcastAdd(chest, item);
            return TransferDone;
        }

        /// <summary>ChestAddItem (5): u64 chest, u64 inventory, u32 length + {bool definition follows, item record}.</summary>
        private static void BroadcastAdd(ulong chest, ChestItemModel item)
        {
            var blob = new List<byte>();
            var bw = new GamePacketWriter(blob);
            bw.Write(false);
            ToPacket(item).WriteRecord(bw);
            byte[] packet = Build(AddItemSub, w =>
            {
                w.Write(chest);
                w.Write(chest);
                w.Write((uint)blob.Count);
                w.WriteBytes(blob.ToArray());
            });
            foreach (WorldSession s in Watchers(chest))
                s.EnqueueMessage(ZoneMessageOpcode.ChestBase, packet);
        }

        /// <summary>ChestDeleteItem (6): u64 chest, u64 inventory, u64 item guid.</summary>
        private static void BroadcastDelete(ulong chest, ulong itemGuid)
        {
            byte[] packet = Build(DeleteItemSub, w =>
            {
                w.Write(chest);
                w.Write(chest);
                w.Write(itemGuid);
            });
            foreach (WorldSession s in Watchers(chest))
                s.EnqueueMessage(ZoneMessageOpcode.ChestBase, packet);
        }

        private static List<WorldSession> Watchers(ulong chest)
        {
            lock (sync)
                return watching.Where(w => w.Value == chest && !w.Key.Disconnected).Select(w => w.Key).ToList();
        }

        /// <summary>Dev/testing: puts items straight into a chest's first free slot.</summary>
        public static void DevPut(ulong chest, uint definitionId, uint count)
        {
            Props.PlacedProp prop = Props.Find(chest);
            uint slots = prop == null ? 0 : Slots(prop.PropId);
            ChestItemModel item;
            lock (sync)
            {
                List<ChestItemModel> list = Of(chest);
                uint? slot = Enumerable.Range(0, (int)slots).Select(s => (uint?)s).FirstOrDefault(s => list.All(i => i.Slot != s));
                if (slot == null)
                    return;
                item = new ChestItemModel { Id = Inventory.Inventory.NewItemGuid(), ChestGuid = chest, ItemDefinitionId = definitionId, Count = count, Slot = slot.Value };
                list.Add(item);
            }
            DatabaseManager.Instance.CharacterDatabase.Save(c => c.ChestItem.Add(item));
            BroadcastAdd(chest, item);
        }

        /// <summary>
        /// Moves a chest's contents to another key: from a placed chest to the bag item it was picked up as, or back to
        /// the chest placed from that item (the live game keeps a picked-up chest's items inside it).
        /// </summary>
        public static void Move(ulong from, ulong to)
        {
            List<ChestItemModel> items;
            lock (sync)
            {
                if (!contents.Remove(from, out items))
                    return;
                foreach (ChestItemModel item in items)
                    item.ChestGuid = to;
                contents[to] = items;
                foreach (var w in watching.Where(w => w.Value == from).Select(w => w.Key).ToList())
                    watching.Remove(w);
            }
            List<ulong> ids = items.Select(i => i.Id).ToList();
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                foreach (ulong id in ids)
                {
                    var model = new ChestItemModel { Id = id };
                    c.ChestItem.Attach(model);
                    model.ChestGuid = to;
                    c.MarkModified(model, nameof(model.ChestGuid));
                }
            }).ContinueWith(t => log.Error(t.Exception, "Failed to move chest contents."),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            log.Info($"Chest contents ({items.Count} stack(s)) moved from 0x{from:X} to 0x{to:X}.");
        }

        public static bool HasItems(ulong chest)
        {
            lock (sync)
                return contents.TryGetValue(chest, out var list) && list.Count > 0;
        }

        /// <summary>
        /// A chest prop is leaving the world (picked up, or its build site deleted): its contents go to
        /// <paramref name="to"/>'s bag, or are dropped when nobody takes them.
        /// </summary>
        public static void Empty(ulong chest, WorldSession to)
        {
            List<ChestItemModel> items;
            lock (sync)
            {
                if (!contents.Remove(chest, out items))
                    return;
                foreach (var w in watching.Where(w => w.Value == chest).Select(w => w.Key).ToList())
                    watching.Remove(w);
            }
            foreach (ChestItemModel item in items)
                to?.GiveItem(item.ItemDefinitionId, item.Count);
            DatabaseManager.Instance.CharacterDatabase.Save(c => c.ChestItem.RemoveRange(c.ChestItem.Where(i => i.ChestGuid == chest)))
                .ContinueWith(t => log.Error(t.Exception, "Failed to empty chest."),
                    System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            log.Info($"Chest 0x{chest:X} emptied ({items.Count} stack(s)){(to != null ? $" into {to.CharacterName}'s bag" : "")}.");
        }

        /// <summary>
        /// A lapsed site's chest comes back through Build Site Contents: its items go to the owner's overflow tab (wiki
        /// Chests: "the items from the chest are removed and placed in the overflow inventory, along with the chest").
        /// Returns how many stacks moved.
        /// </summary>
        public static int EmptyToOverflow(ulong chest, WorldSession owner)
        {
            List<ChestItemModel> items;
            lock (sync)
            {
                if (!contents.Remove(chest, out items))
                    return 0;
                foreach (var w in watching.Where(w => w.Value == chest).Select(w => w.Key).ToList())
                    watching.Remove(w);
            }
            foreach (ChestItemModel item in items)
                foreach ((CharacterItemModel changed, bool isNew) in owner.Inventory.AddToOverflow(item.ItemDefinitionId, item.Count))
                    owner.SendItem(changed, replace: !isNew);
            DatabaseManager.Instance.CharacterDatabase.Save(c => c.ChestItem.RemoveRange(c.ChestItem.Where(i => i.ChestGuid == chest)))
                .ContinueWith(t => log.Error(t.Exception, "Failed to empty chest."),
                    System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            log.Info($"Chest 0x{chest:X}: {items.Count} stack(s) moved to {owner.CharacterName}'s overflow.");
            return items.Count;
        }

        public static void Forget(WorldSession session)
        {
            lock (sync)
                watching.Remove(session);
        }

        private static byte[] Build(uint sub, Action<GamePacketWriter> body)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(sub);
            body(writer);
            return output.ToArray();
        }
    }
}
