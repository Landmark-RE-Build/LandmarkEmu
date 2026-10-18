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

        /// <summary>InventoryPacketEquipByItemRecord (InventoryBase 0x2D sub 8, 0.695 FUN_142fcbbb0) [P].</summary>
        private const ushort EquipByItemRecordSub = 0x08;

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
                        : HandleTransfer(session, transfer) ? Game.Chests.TransferDone
                        // A refusal with its own client message (0x22 unique-equip dialog, TASKS 6.54), else the generic one.
                        : session.Inventory.LastRefusal != 0 ? session.Inventory.LastRefusal : Game.Chests.TransferFailedInInventory;
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
                    foreach ((var item, bool deleted, bool added) in moved)
                        if (deleted)
                            session.EnqueueMessage(new ClientUpdateItemDelete { Guid = item.Id });
                        else
                            session.SendItem(item, replace: !added);
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
                            // A destroyed chest that still held items: its contents go with it (they were keyed to its
                            // guid, which nothing holds any more, and stayed in the chest table and in memory for good).
                            if (session.Inventory.Get(guid) == null && Game.Chests.HasItems(guid))
                                Game.Chests.Empty(guid, null);
                            session.SendEquipmentChanges(equipmentBefore);
                            log.Info($"{session.CharacterName} destroyed {count} × item {taken.ItemDefinitionId}.");
                        }
                    }
                    break;
                case InventoryRequest.ToggleAutoResupply:
                    if (request.Body.Length >= 9)
                        HandleToggleAutoResupply(session, BitConverter.ToUInt64(request.Body, 0), request.Body[8] != 0);
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
                case EquipByItemRecordSub:
                    // InventoryPacketEquipByItemRecord {List<ItemRecord>} (FUN_142fcbbb0): sent by the store screen's
                    // EquipStoreBundle (FUN_1435e8d40 → FUN_14324b820, a bundle of the Daybreak-served store pages) and the
                    // requisition reply handler FUN_1431d3260; no shipped UI calls it. Logged until decisions 6.59 / 6.60
                    // settle the store (L137; then: equip each listed record the player owns via HandleEquip).
                    log.Info($"{session.CharacterName}: EquipByItemRecord ({request.Body.Length} bytes) not handled (store pending).");
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
            // A vault item is worn only while the vault is open within reach (as TransferItem): right-click Equip names
            // the item by guid alone, which reached the vault from anywhere.
            if (item == null || (item.Container == Game.Inventory.Inventory.VaultContainer && !Game.Vaults.CanAccess(session)))
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

        /// <summary>
        /// InventoryPacketToggleAutoResupply (Inventory 0x2D sub 0x0D) {u64 item guid, bool on} [P: writer FUN_142f18aa0
        /// writes the u16 family and sub (FUN_142f169d0), the guid (FUN_142c128f0) and one byte]. Sent by the client's
        /// CoinStoreScreen.TurnOnAutoResupply / TurnOffAutoResupply Lua natives (FUN_1435eeb90 / FUN_1435eec10 →
        /// FUN_142f261b0) with an item definition id: the client takes the first stack of that definition (its
        /// definition map, keyed at ClientItem +0x178 by FUN_142ced2c0 from +0x8), returns at once when the definition
        /// fails its check FUN_142f1d360 (<see cref="Game.Inventory.ItemDefinitionManager.ItemDefinition.AutoResupplyAllowed"/>)
        /// and sends only a change, after setting its own +0xdc [P]. No shipped UI or script calls either native (0.589
        /// and 0.695 AS3, Resources/Scripts) and no live definition passes the check, so a stock client never sends it;
        /// the switch is still kept as live kept it (items.auto_resupply). Nothing on the client acts on +0xdc beyond
        /// keeping it (readers: the item record readers / writers / copies and the uncalled getter FUN_142f21ff0) [P];
        /// what live's server did with it (PS2's auto-resupply re-bought a consumable at the depot) is not known yet
        /// (OPEN-LEADS). No reply: the client expects none.
        /// </summary>
        internal static void HandleToggleAutoResupply(WorldSession session, ulong itemGuid, bool on)
        {
            CharacterItemModel item = session.Inventory.Get(itemGuid);
            // The client's own check (FUN_142f1d360) on the item's definition: a toggle it would never send is refused.
            if (item == null || Game.Inventory.ItemDefinitionManager.Get(item.ItemDefinitionId) is not { AutoResupplyAllowed: true })
            {
                log.Debug($"{session.CharacterName}: auto-resupply {(on ? "on" : "off")} for item 0x{itemGuid:X} refused (no such item or not allowed).");
                return;
            }
            if (session.Inventory.SetAutoResupply(item, on))
                log.Info($"{session.CharacterName}: auto-resupply {(on ? "on" : "off")} for item {item.ItemDefinitionId} (0x{itemGuid:X}).");
        }

        /// <summary>Using an item from the inventory: prop items start placing their prop.</summary>
        internal static void HandleActivate(WorldSession session, ulong itemGuid)
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
            // A prop item starts its placement; build mode opens for it (PLACE_OPENS_BUILD_MODE, Game/BuildModeHolster).
            if (propId != 0)
                Game.BuildModeHolster.OpenForPlacement(session, Game.Props.BeginPlacement(session, propId, item.ItemDefinitionId, item.Id));
            else
                log.Debug($"{session.CharacterName} activated item {item.ItemDefinitionId}, which has no use yet.");
        }

        /// <summary>
        /// Tooltip requests: 0x11 by item definition (u32 id, then a flag byte); 0x0F / 0x10 by item: an item location
        /// (owner type 1 = no owner guid), then the item guid — the requester's own item, or another player's equipped
        /// item when the location names them (the Inspect window).
        /// </summary>
        private static void HandleTooltip(WorldSession session, InventoryRequest request)
        {
            bool byDefinition = request.SubOpcode == InventoryRequest.ItemDefinitionTooltipRequest;
            uint definitionId;
            ulong guid = 0;
            bool full = request.SubOpcode == InventoryRequest.ItemInstanceFullTooltipRequest;
            ulong fraudTimer = 0;
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
                ItemLocation location = ItemLocation.Read(reader);
                if (reader.BytesRemaining < 8)
                    return;
                guid = reader.ReadULong();
                CharacterItemModel item = session.Inventory.Get(guid);
                // The Inspect window's gear (0.695, EquipmentItemRenderer → FUN_1435de7d0): the location names the
                // inspected player; only their equipped items, which StartInspect showed (TASKS 3.22).
                if (item == null && location.OwnerGuid != 0 && location.OwnerGuid != session.PlayerGuid
                    && Game.PlayerManager.FindByGuid(location.OwnerGuid) is WorldSession owner && owner.Inventory?.Get(guid) is { } theirs
                    && theirs.Container == Game.Inventory.Inventory.EquippedContainer)
                    item = theirs;
                if (item == null)
                {
                    log.Debug($"Tooltip 0x{request.SubOpcode:X2} for unknown item {guid:X} ({Convert.ToHexString(request.Body)}).");
                    return;
                }
                definitionId = item.ItemDefinitionId;
                // A store purchase still waiting out StoreTradeWaitHours: the tooltip's "Tradable in %TIME" (L184).
                fraudTimer = InventoryTooltipReply.FraudTimerOf(item.TradeableAfter, DateTime.UtcNow);
            }

            var definition = Game.Inventory.ItemDefinitionManager.Get(definitionId);
            if (definition?.RawData == null)
                return;
            session.EnqueueMessage(ZoneMessageOpcode.InventoryBase,
                InventoryTooltipReply.Build(byDefinition, full, definition, guid, fraudTimer));
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
            inventory.LastRefusal = 0;
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
            CharacterItemModel wanted = inventory.Get(request.ItemGuid);
            // The same vault rule for the items themselves: a request naming an empty bag slot (or a worn slot, for the
            // tool switch) finds its item by guid, so "bag slot → bag" carrying a vault item's guid took it out of the vault
            // from anywhere, and the hotbar tool switch could wield a tool stored in the vault.
            if ((item.Container == Game.Inventory.Inventory.VaultContainer || wanted?.Container == Game.Inventory.Inventory.VaultContainer)
                && !Game.Vaults.CanAccess(session))
            {
                log.Debug($"{session.CharacterName}: transfer of a vault item refused (no vault open within reach).");
                return false;
            }

            var equipmentBefore = session.VisibleEquipment();
            bool sameSpot = request.Source.Container == request.Destination.Container
                && request.Source.Slot == request.Destination.Slot;
            List<CharacterItemModel> moved;
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
            // A right-clicked tool also goes on the action bar (loading tip 351, still in the 0.695 Tips.txt: "Right-
            // Clicking a tool in your Inventory will send it directly to your hotbar for use.").
            if (moved.Count > 0 && IsRightClickEquip(request) && wanted?.Container == Game.Inventory.Inventory.EquippedContainer)
                session.PlaceOnActionBar(wanted);
            return moved.Count > 0;
        }

        /// <summary>
        /// The TransferItem a right-click on a bag item sends when the item is a main-hand item (definition +0x34 = 2:
        /// pick, axe, extractor, the other gathering tools, weapons) or matches the active tool (+0x16c): Lua
        /// ClientInventory.ActivateItem (ItemContainerBase.as:303-316, button 1) → FUN_1435dd400 → FUN_143457220 →
        /// FUN_142f24db0(inventory, guid, in bag, -1) = destination {equipped, any slot}, source {bag, any slot}, the
        /// guid, quantity -1, no swap [P]. The hotbar's own tool use (FUN_14345af10 → FUN_14345afe0) and an armour
        /// right-click name the main-hand slot and swap instead, a drag names both slots: none of them is this.
        /// In 0.589 the same branch put the item on the action bar on the client (FUN_1433aa0c0 → FUN_1433a0b80);
        /// 0.695 replaced that call with this equip, so the action bar slot is the server's part (<see
        /// cref="WorldSession.PlaceOnActionBar"/>) [I: live's reply is not captured; the tip and the 0.589 client say what
        /// the player saw].
        /// </summary>
        internal static bool IsRightClickEquip(TransferItemRequest request)
            => request.Destination.Container == Game.Inventory.Inventory.EquippedContainer
               && request.Destination.Slot == TransferItemRequest.AnySlot
               && request.Source.Container == Game.Inventory.Inventory.BagContainer
               && request.Source.Slot == TransferItemRequest.AnySlot
               && !request.Swap;
    }
}
