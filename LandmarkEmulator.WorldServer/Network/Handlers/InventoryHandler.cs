using System;
using System.Collections.Generic;
using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;

namespace LandmarkEmulator.WorldServer.Network.Handlers
{
    /// <summary>
    /// Inventory requests (InventoryBase 0x2D, u16 sub): moving items (drag, hotbar tool switch, right-click equip and
    /// unequip), deleting, using items, tooltips. Item changes go back as ItemUpdate; equipment changes are broadcast.
    /// </summary>
    public class InventoryHandler
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Routes one inventory request by sub-opcode. Every TransferItem (0x16) gets a TransferItemResponse (0x19): the
        /// request echoed plus a u32 result (0 = done); the client waits for it.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.InventoryBase)]
        public static void HandleInventory(WorldSession session, InventoryRequest request)
        {
            switch (request.SubOpcode)
            {
                case InventoryRequest.RequestTransferItem:
                    log.Debug($"TransferItem from {session.CharacterName}: {BitConverter.ToString(request.Body)}");
                    TransferItemRequest transfer = TransferItemRequest.Read(request.Body);
                    uint result = transfer.Source.OwnerType == Game.Chests.ChestOwnerType || transfer.Destination.OwnerType == Game.Chests.ChestOwnerType
                        ? Game.Chests.Transfer(session, transfer)
                        : HandleTransfer(session, transfer) ? Game.Chests.TransferDone : Game.Chests.TransferFailedInInventory;
                    // TransferItemResponse (0x19, reader FUN_142e78a30): the request echoed, then a u32 result — the
                    // client's "Item Transfer:" reasons (FUN_143b966c0, 42 names; 1 = success). Some show a message:
                    // 0x16 / 0x17 Chest.AccessDeniedOnItemAction, 0x20 / 0x21 no-trade, 0x12 fraud timer, 0x25 a chest
                    // holding items into a chest, 0x22 the unique-equip dialog. The client waits for it before sending
                    // the second half of a tool switch.
                    byte[] response = new byte[2 + request.Body.Length + 4];
                    BitConverter.TryWriteBytes(response.AsSpan(0, 2), (ushort)0x19);
                    request.Body.CopyTo(response, 2);
                    BitConverter.TryWriteBytes(response.AsSpan(2 + request.Body.Length), result);
                    session.EnqueueMessage(ZoneMessageOpcode.InventoryBase, response);
                    break;
                case InventoryRequest.ItemInstanceTooltipRequest:
                case InventoryRequest.ItemInstanceFullTooltipRequest:
                case InventoryRequest.ItemDefinitionTooltipRequest:
                    HandleTooltip(session, request);
                    break;
                case InventoryRequest.RequestOverflowAbsorb:
                {
                    var moved = session.Inventory.AbsorbOverflow();
                    foreach ((var item, bool deleted) in moved)
                        if (deleted)
                            session.EnqueueMessage(new ClientUpdateItemDelete { Guid = item.Id });
                        else
                            session.SendItem(item, replace: true);
                    if (session.Inventory.HasOverflow)
                        Game.Chat.System(session, "Your bag is full; some items are still in overflow.");
                    log.Info($"{session.CharacterName} retrieved overflow: {moved.Count} stacks changed.");
                    break;
                }
                case InventoryRequest.DeleteItem:
                    // RequestDeleteItem (client FUN_142e85xxx): u64 item guid, u32 count, u32 mode.
                    if (request.Body.Length >= 12)
                    {
                        ulong guid = BitConverter.ToUInt64(request.Body, 0);
                        uint count = BitConverter.ToUInt32(request.Body, 8);
                        var equipmentBefore = session.VisibleEquipment();
                        if (session.TakeFromStack(guid, count) is { } taken)
                        {
                            session.SendEquipmentChanges(equipmentBefore);
                            log.Info($"{session.CharacterName} destroyed {count} × item {taken.ItemDefinitionId}.");
                        }
                    }
                    break;
                case InventoryRequest.RearrangeItemSlots:
                    // The bag's "sort by" (ClientInventory.SetItemsToSlots): u32 container, u32 n, n × {u32 slot, u64 guid}.
                    if (request.Body.Length >= 8)
                    {
                        uint container = BitConverter.ToUInt32(request.Body, 0);
                        uint n = BitConverter.ToUInt32(request.Body, 4);
                        var order = new List<(uint, ulong)>();
                        for (int i = 0, o = 8; i < n && o + 12 <= request.Body.Length; i++, o += 12)
                            order.Add((BitConverter.ToUInt32(request.Body, o), BitConverter.ToUInt64(request.Body, o + 4)));
                        uint capacity = container == Game.Inventory.Inventory.BagContainer ? session.Inventory.BagSize
                            : container == Game.ItemUses.VaultContainer ? Game.ItemUses.VaultSizeOf(session.PlayerGuid) : 0;
                        var changed = session.Inventory.Rearrange(container, order, capacity);
                        foreach (CharacterItemModel item in changed)
                            session.SendItem(item, replace: true);
                        log.Info($"{session.CharacterName} sorted container {container}: {order.Count} listed, {changed.Count} moved.");
                    }
                    break;
                case InventoryRequest.EquipByGuid:
                    // Right-click "Equip" (Lua EquipItem → FUN_14331fc60): u64 item guid, u32 profile, u32 equip slot.
                    if (request.Body.Length >= 16)
                        HandleEquip(session, BitConverter.ToUInt64(request.Body, 0), BitConverter.ToUInt32(request.Body, 12));
                    break;
                case InventoryRequest.EquippedRemove:
                    // Unequip (same Lua call with no item): u32 equip slot, u32 profile.
                    if (request.Body.Length >= 4)
                        HandleUnequip(session, BitConverter.ToUInt32(request.Body, 0));
                    break;
                case InventoryRequest.ActivateItem:
                    if (request.Body.Length >= 8)
                        HandleActivate(session, BitConverter.ToUInt64(request.Body, 0));
                    break;
                default:
                    Network.OpcodeStats.Unanswered("Inventory", request.SubOpcode);
                    log.Debug($"Unhandled inventory request 0x{request.SubOpcode:X2} ({request.Body.Length} bytes).");
                    break;
            }
        }

        /// <summary>Puts an item into an equipment slot (EquipmentSlotDefinitions id); what was there swaps to the bag.</summary>
        /// <summary>Equips an item from code (build mode bringing the held item back).</summary>
        public static void Equip(WorldSession session, ulong itemGuid, uint equipSlotId) => HandleEquip(session, itemGuid, equipSlotId);

        /// <summary>Unequips a slot from code (build mode putting the held item away).</summary>
        public static void Unequip(WorldSession session, uint equipSlotId) => HandleUnequip(session, equipSlotId);

        private static void HandleEquip(WorldSession session, ulong itemGuid, uint equipSlotId)
        {
            CharacterItemModel item = session.Inventory.Get(itemGuid);
            if (item == null)
                return;
            uint? slot = Game.Inventory.Inventory.EquippedSlotFor(equipSlotId);
            if (slot == null)
                return;
            var equipmentBefore = session.VisibleEquipment();
            List<CharacterItemModel> moved = session.Inventory.MoveTo(item, Game.Inventory.Inventory.EquippedContainer, slot.Value);
            if (moved.Count == 0)
                log.Debug($"Equip of {item.ItemDefinitionId} into equipment slot {equipSlotId} refused.");
            foreach (CharacterItemModel changed in moved)
                session.SendItem(changed, replace: true);
            session.SendEquipmentChanges(equipmentBefore);
        }

        /// <summary>Takes whatever is in an equipment slot back to the bag.</summary>
        private static void HandleUnequip(WorldSession session, uint equipSlotId)
        {
            uint? slot = Game.Inventory.Inventory.EquippedSlotFor(equipSlotId);
            CharacterItemModel item = slot == null ? null : session.Inventory.At(Game.Inventory.Inventory.EquippedContainer, slot.Value);
            if (item == null)
                return;
            var equipmentBefore = session.VisibleEquipment();
            List<CharacterItemModel> moved = session.Inventory.MoveTo(item, Game.Inventory.Inventory.BagContainer, TransferItemRequest.AnySlot);
            foreach (CharacterItemModel changed in moved)
                session.SendItem(changed, replace: true);
            session.SendEquipmentChanges(equipmentBefore);
        }

        /// <summary>Using an item from the inventory: prop items start placing their prop.</summary>
        private static void HandleActivate(WorldSession session, ulong itemGuid)
        {
            CharacterItemModel item = session.Inventory.Get(itemGuid);
            if (item == null)
                return;
            if (Game.RecipeBook.Learn(session, item.ItemDefinitionId))
                return;
            if (Game.ItemUses.TryActivate(session, item) || Game.Titles.TryUseItem(session, item) || Game.SiteContents.TryUse(session, item)
                || Game.DesignItems.TryUse(session, item))
                return;
            uint propId = Game.Inventory.ItemDefinitionManager.Get(item.ItemDefinitionId)?.PropId ?? 0;
            if (propId != 0)
                Game.Props.BeginPlacement(session, propId, item.ItemDefinitionId, item.Id);
            else
                log.Debug($"{session.CharacterName} activated item {item.ItemDefinitionId}, which has no use yet.");
        }

        /// <summary>
        /// Tooltip requests: 0x11 by item definition (u32 id, then a flag byte); 0x0F / 0x10 by item: an item location
        /// (owner type 1 = no owner guid), then the item guid.
        /// </summary>
        private static void HandleTooltip(WorldSession session, InventoryRequest request)
        {
            bool byDefinition = request.SubOpcode == InventoryRequest.ItemDefinitionTooltipRequest;
            uint definitionId;
            ulong guid = 0;
            bool full = request.SubOpcode == InventoryRequest.ItemInstanceFullTooltipRequest;
            if (byDefinition)
            {
                if (request.Body.Length < 4)
                    return;
                definitionId = BitConverter.ToUInt32(request.Body, 0);
                full = request.Body.Length > 4 && request.Body[4] != 0;
            }
            else
            {
                var reader = new LandmarkEmulator.Shared.Network.GamePacketReader(request.Body);
                ItemLocation.Read(reader);
                if (reader.BytesRemaining < 8)
                    return;
                guid = reader.ReadULong();
                CharacterItemModel item = session.Inventory.Get(guid);
                if (item == null)
                {
                    log.Debug($"Tooltip 0x{request.SubOpcode:X2} for unknown item {guid:X} ({Convert.ToHexString(request.Body)}).");
                    return;
                }
                definitionId = item.ItemDefinitionId;
            }

            var definition = Game.Inventory.ItemDefinitionManager.Get(definitionId);
            if (definition?.RawData == null)
                return;
            session.EnqueueMessage(ZoneMessageOpcode.InventoryBase,
                InventoryTooltipReply.Build(byDefinition, full, definition, guid));
        }

        private static bool IsStorage(uint container)
            => container == Game.Inventory.Inventory.BagContainer || container == Game.Inventory.Inventory.VaultContainer;

        private static bool IsMatchingStack(Game.Inventory.Inventory inventory, CharacterItemModel item, ItemLocation destination)
            => destination.Slot != TransferItemRequest.AnySlot && inventory.At(destination.Container, destination.Slot) is { } target
               && target != item && target.ItemDefinitionId == item.ItemDefinitionId;

        /// <summary>
        /// TransferItem: the first location is the destination, the second the source. Handles bag moves and swaps, the
        /// hotbar tool switch (one request meaning "wield this, put the held tool away"), deselecting the held tool, and
        /// right-click equip/unequip (where the client names slots that don't fit; see <see cref="Game.Inventory.Inventory.MoveTo"/>).
        /// Returns whether anything moved.
        /// </summary>
        private static bool HandleTransfer(WorldSession session, TransferItemRequest request)
        {
            Game.Inventory.Inventory inventory = session.Inventory;
            // The vault only while one is open within reach (Game.Vaults).
            if ((request.Source.Container == Game.Inventory.Inventory.VaultContainer
                 || request.Destination.Container == Game.Inventory.Inventory.VaultContainer) && !Game.Vaults.CanAccess(session))
            {
                log.Debug($"{session.CharacterName}: vault transfer refused (no vault open within reach).");
                return false;
            }
            // A concrete source slot names the item to move. The guid can be a different item: switching tools sends
            // "main hand → bag" carrying the guid of the tool about to be wielded. The guid only identifies the item
            // when the source slot is "any".
            CharacterItemModel item = request.Source.Slot != TransferItemRequest.AnySlot
                ? inventory.At(request.Source.Container, request.Source.Slot) ?? inventory.Get(request.ItemGuid)
                : inventory.Get(request.ItemGuid);
            if (item == null)
                return false;

            var equipmentBefore = session.VisibleEquipment();
            bool sameSpot = request.Source.Container == request.Destination.Container
                && request.Source.Slot == request.Destination.Slot;
            List<CharacterItemModel> moved;
            CharacterItemModel wanted = inventory.Get(request.ItemGuid);
            ItemLocation? equipSlot = request.Destination.Container == Game.Inventory.Inventory.EquippedContainer ? request.Destination
                : request.Source.Container == Game.Inventory.Inventory.EquippedContainer ? request.Source
                : null;
            if (!sameSpot && wanted != null && equipSlot != null)
            {
                // Action bar use of a tool. The guid is the tool the player picked; one location is its equipment
                // slot. The tool is wielded and whatever was held swaps into the bag (switching sends one request:
                // held slot → bag, guid = new tool). Picking the tool already held keeps it held, matching the
                // client, which keeps that slot selected.
                bool held = wanted.Container == Game.Inventory.Inventory.EquippedContainer && wanted.Slot == equipSlot.Value.Slot;
                bool toBag = request.Destination.Container != Game.Inventory.Inventory.EquippedContainer;
                // Esc (put the tool away) sends "held slot → bag" naming the held tool twice. By the second the hand is
                // already empty: it's the repeat, not a switch to that tool (handled as one, the tool came straight back).
                if (toBag && !held && inventory.At(Game.Inventory.Inventory.EquippedContainer, equipSlot.Value.Slot) == null)
                    return true;
                if (held && toBag)
                    // Deselecting the hotbar slot of the held tool: "held slot → bag" naming that same tool.
                    moved = inventory.MoveTo(wanted, request.Destination.Container, request.Destination.Slot);
                else
                {
                    moved = held ? new() : inventory.MoveTo(wanted, Game.Inventory.Inventory.EquippedContainer, equipSlot.Value.Slot);
                    if (held)
                        return true;
                }
            }
            else if (!sameSpot && request.Quantity > 0 && IsStorage(request.Source.Container) && IsStorage(request.Destination.Container)
                && (request.Quantity < item.Count || IsMatchingStack(inventory, item, request.Destination)))
            {
                // Part of a stack (split dialog / Ctrl-drag), or a stack dropped onto the same item: split or merge.
                var counted = inventory.MoveCount(item, request.Destination.Container, request.Destination.Slot, request.Quantity);
                foreach ((CharacterItemModel changed, bool deleted) in counted)
                    if (deleted)
                        session.EnqueueMessage(new ClientUpdateItemDelete { Guid = changed.Id });
                    else
                        session.SendItem(changed, replace: true);
                return counted.Count > 0;
            }
            else
                moved = sameSpot
                    ? inventory.ToggleEquip(item)
                    : inventory.MoveTo(item, request.Destination.Container, request.Destination.Slot);
            if (moved.Count == 0)
                log.Debug($"Transfer of {item.ItemDefinitionId} to {request.Destination.Container}/{request.Destination.Slot} refused.");

            foreach (CharacterItemModel changed in moved)
                session.SendItem(changed, replace: true);
            session.SendEquipmentChanges(equipmentBefore);
            return moved.Count > 0;
        }
    }
}
