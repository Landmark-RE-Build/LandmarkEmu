using LandmarkEmulator.WorldServer.Storage;
using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.WorldServer.Network.Message.Model.Shared;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer.Game.Inventory
{
    /// <summary>
    /// A character's items: loaded from the database, sent in SendSelfToClient, changed by mining and crafting, and
    /// saved back. Containers follow the live game: 0 bag, 2 equipped, 5 resources, 8 currency.
    /// </summary>
    public class Inventory
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const uint BagContainer = 0;
        public const uint EquippedContainer = 2;
        public const uint ResourceContainer = 5;
        public const uint CurrencyContainer = 8;
        /// <summary>
        /// Overflow (client ContainerId.OVERFLOW, data source Character.Overflow, the inventory's third tab): items that
        /// didn't fit in the bag wait here until "Retrieve all" (ClientInventory.MoveOverflow, InventoryBase 0x1C
        /// RequestOverflowAbsorb), for <c>OverflowExpiryHours</c> (default 24; wiki Inventory: "you must remove anything
        /// within in 24 hours or it will be unrecoverable" [W]); then they are deleted (<see cref="ExpireOverflow"/>,
        /// Game/OverflowExpiry.cs). The client shows the countdown itself [P]: an overflow item's record carries its
        /// seconds left (+0x138, timestamped on read, FUN_142c208e0), the Character.Overflow rows get "ExpirationTime" =
        /// seconds left - (now - read time) (0.695 FUN_142f258f0, owner type 1 container 7 only) and the Inventory window
        /// titles the tab "Overflow: Items expire in H:MM:SS" (Inventory SWF updateOverflowTime).
        /// </summary>
        public const uint OverflowContainer = 7;
        private const uint MaxOverflowSlots = 500;

        /// <summary>
        /// The game's currency: Lumens (item 1579, StringHashToValue Currency.ItemDefinitionId / ClaimVendor
        /// .CurrencyItemId). The client counts the stack of it in the currency container for the inventory's coin
        /// slot, mail's "send money" / cash on delivery (FUN_143588e70) and trade coin offers.
        /// </summary>
        public const uint CurrencyItem = 1579;

        /// <summary>
        /// The client's weapon slots (StringHashToValue: PrimaryWeaponSlotName "Main Hand", SecondaryWeaponSlotName "Off
        /// Hand" = EquipmentSlotDefinitions 2 / 11; read by FUN_142d12360 into the slot manager +0x1f0 / +0x1f4 through
        /// FUN_143d49c90) [P]. No item is equipped in the off hand: it shows the main-hand item's second model
        /// (<see cref="ItemDefinitionManager.ItemDefinition.SecondaryModelFile"/>).
        /// </summary>
        public const uint MainHandSlot = 2, OffHandSlot = 11;

        /// <summary>
        /// Bag size of a new character (the live game's default bag held 40); Inventory Expansions raise it by 20 up to
        /// 100 (stored as an <see cref="Unlocks.BagSize"/> row).
        /// </summary>
        public const uint DefaultBagSize = 40, MaxBagSize = 100;

        /// <summary>This character's bag size (slots 0 … BagSize-1).</summary>
        public uint BagSize { get; private set; }

        private const uint ResourceInventoryType = 5;

        /// <summary>
        /// Starting items, as a live character had them: tools in the bag, gear equipped.
        /// </summary>
        private static readonly (uint DefinitionId, uint Count, uint Container, uint Slot)[] StarterKit =
        {
            (3253, 1, BagContainer, 0),       // Adventurer's Mining Pick
            (3259, 1, BagContainer, 1),       // Adventurer's Logging Axe
            (231, 10, BagContainer, 2),       // Lightstone
            (3894, 5, BagContainer, 3),       // Cozy Campfire
            (2178, 1, EquippedContainer, 7),  // Adventurer's Sprint Boots
            (1178, 1, EquippedContainer, 8),  // Adventurer's Grappling Hook
        };

        private static ulong nextItemGuid;
        private static Dictionary<uint, (uint Slot, uint Max)> resourceSlots;

        private readonly ulong characterId;
        private readonly List<CharacterItemModel> items;

        public IReadOnlyList<CharacterItemModel> Items => items;

        public Inventory(CharacterModel character)
        {
            characterId = character.Id;
            BagSize = Unlocks.Values(characterId, Unlocks.BagSize).DefaultIfEmpty(DefaultBagSize).Max();
            items = character.Items.ToList();
            // The starter kit goes to a new character once (Unlocks kind 34). Keyed on "no items" alone, any character who
            // had destroyed or handed away everything (the kit itself included) got a fresh kit at every login. A character
            // that already has items is marked now, so one emptied later never qualifies.
            bool kitGiven = Unlocks.Values(characterId, Unlocks.StarterKitGiven).Count > 0;
            if (items.Count == 0 && !kitGiven)
                GiveStarterKit();
            if (!kitGiven)
                Unlocks.Add(characterId, Unlocks.StarterKitGiven, 1);
            RehomeMisplacedItems();
            // Overflow items whose time ran out while the player was away go before the client sees them (a chest item's
            // contents with it, as OverflowExpiry does for online players).
            List<CharacterItemModel> expired = ExpireOverflow();
            foreach (CharacterItemModel item in expired)
                if (Chests.HasItems(item.Id))
                    Chests.Empty(item.Id, null);
            if (expired.Count > 0)
                log.Info($"Character {characterId}: {expired.Count} overflow stack(s) expired.");
        }

        /// <summary>Hours an overflow item waits before it is deleted (setting OverflowExpiryHours; 0 = kept).</summary>
        private static uint OverflowExpiryHours => ConfigurationManager<WorldServerConfiguration>.Instance.Config.OverflowExpiryHours;

        /// <summary>
        /// While overflow expires: an arrival tops up an overflow stack of the same item whose end is at most this much
        /// before its own (<see cref="AddOverflow"/>), so items arriving within a minute share one stack and countdown.
        /// </summary>
        internal static readonly TimeSpan SameArrivalWindow = TimeSpan.FromMinutes(1);

        /// <summary>When an item put in overflow now expires, or null when overflow items are kept.</summary>
        private static DateTime? NewOverflowExpiry() => OverflowExpiryHours > 0 ? DateTime.UtcNow.AddHours(OverflowExpiryHours) : null;

        /// <summary>
        /// Deletes the overflow stacks whose time is up and returns them (the caller tells an online client and clears a
        /// chest item's contents). Overflow items without a time (from before the setting, or moved there by an admin)
        /// get one now. Nothing happens with the setting at 0.
        /// </summary>
        public List<CharacterItemModel> ExpireOverflow()
        {
            var gone = new List<CharacterItemModel>();
            if (OverflowExpiryHours == 0)
                return gone;
            DateTime now = DateTime.UtcNow;
            foreach (CharacterItemModel item in items.Where(i => i.Container == OverflowContainer).ToList())
            {
                if (item.ExpiresAt == null)
                {
                    SetExpiresAt(item, NewOverflowExpiry());
                    continue;
                }
                if (item.ExpiresAt > now)
                    continue;
                items.Remove(item);
                ulong id = item.Id;
                Save(c => { var m = new CharacterItemModel { Id = id }; c.CharacterItem.Attach(m); c.CharacterItem.Remove(m); });
                gone.Add(item);
            }
            return gone;
        }

        /// <summary>Sets when an overflow item is deleted (dev giveoverflow's minutes; the next ItemAdd tells the client).</summary>
        public void SetExpiresAt(CharacterItemModel item, DateTime? when)
        {
            item.ExpiresAt = when;
            ulong id = item.Id;
            Save(c => { var m = new CharacterItemModel { Id = id }; c.CharacterItem.Attach(m); m.ExpiresAt = when; c.MarkModified(m, nameof(m.ExpiresAt)); });
        }

        /// <summary>
        /// Puts any bag or equipped item whose saved location is impossible (out of range, wrong equipment slot,
        /// a second worn copy of a unique item, or sharing a slot) back into a free bag slot, so a bad save can't hide an item from the client.
        /// </summary>
        private void RehomeMisplacedItems()
        {
            var taken = new HashSet<(uint, uint)>();
            var misplaced = new List<CharacterItemModel>();
            // A second worn copy of a UniqueEquip item (saved before that rule, TASKS 6.54) goes back to the bag too.
            var wornUnique = new HashSet<uint>();
            foreach (CharacterItemModel item in items)
            {
                bool valid = item.Container switch
                {
                    BagContainer => item.Slot < BagSize,
                    EquippedContainer => Fits(item, item.Slot)
                        && (ItemDefinitionManager.Get(item.ItemDefinitionId) is not { UniqueEquip: true } || wornUnique.Add(item.ItemDefinitionId)),
                    _ => true
                };
                if (!valid || !taken.Add((item.Container, item.Slot)))
                    misplaced.Add(item);
            }

            foreach (CharacterItemModel item in misplaced)
            {
                uint? free = FirstFreeBagSlot();
                if (free == null)
                    break;
                log.Warn($"Item {item.Id} ({item.ItemDefinitionId}) was at {item.Container}/{item.Slot}; moved to bag slot {free}.");
                SetLocation(item, BagContainer, free.Value);
            }
        }

        /// <summary>
        /// A new, unique item guid. Continues from the highest guid in the database and starts well above character ids,
        /// so an item guid can never be mistaken for a character.
        /// </summary>
        public static ulong NewItemGuid()
        {
            if (Interlocked.Read(ref nextItemGuid) == 0)
            {
                var db = DatabaseManager.Instance.CharacterDatabase;
                ulong start;
                if (db.SeparateWorld)
                {
                    // Worlds sharing the character database each take guids from their own range (world n: n × 2^28 up
                    // to the next), so two running worlds never create the same guid. The ranges stay below 2^32.
                    ulong low = ItemGuidRange * ConfigurationManager<WorldServerConfiguration>.Instance.Config.ZoneServerId;
                    start = Math.Max(db.GetMaxItemGuid(low, low + ItemGuidRange), low);
                }
                else
                    // Start well above character ids so item and character guids can't be confused.
                    start = Math.Max(db.GetMaxItemGuid(), 0x1000000ul);
                Interlocked.CompareExchange(ref nextItemGuid, start, 0);
            }
            return Interlocked.Increment(ref nextItemGuid);
        }

        /// <summary>Size of each world's item guid range when worlds share the character database.</summary>
        private const ulong ItemGuidRange = 0x10000000ul;

        private static Dictionary<uint, (uint Slot, uint Max)> ResourceSlots
        {
            get
            {
                return resourceSlots ??= GameTableManager.Instance.InventoryDefinitions.Entries
                    .Where(e => e.InventoryType == ResourceInventoryType)
                    .GroupBy(e => e.SlotId)
                    .ToDictionary(g => g.Key, g => (g.First().SlotNumber, g.First().SlotValue));
            }
        }

        /// <summary>
        /// True if the item goes in the resource bag (ores, stone, wood, gems...).
        /// </summary>
        public static bool IsResource(uint definitionId) => ResourceSlots.ContainsKey(definitionId);

        /// <summary>
        /// Gives a new character the starter items (tools and basics in fixed bag/equipment slots).
        /// </summary>
        private void GiveStarterKit()
        {
            foreach ((uint definitionId, uint count, uint container, uint slot) in StarterKit)
                items.Add(new CharacterItemModel
                {
                    Id = NewItemGuid(),
                    CharacterId = characterId,
                    ItemDefinitionId = definitionId,
                    Count = count,
                    Container = container,
                    Slot = slot
                });

            List<CharacterItemModel> added = items.ToList();
            Save(c => c.CharacterItem.AddRange(added));
            log.Info($"Gave character {characterId} the starter kit ({added.Count} items).");
        }

        /// <summary>
        /// Adds items, stacking onto an existing stack where possible. Returns the stacks that changed, so the caller
        /// can tell the client.
        /// </summary>
        public List<(CharacterItemModel Item, bool IsNew)> Add(uint definitionId, uint count) => Add(definitionId, count, default);

        /// <summary>
        /// Adds items that bring an instance state with them (<see cref="ItemState"/>: an item handed over by a trade,
        /// a mail or a chest keeps its wear). Items with a state never top up another stack (a worn tool stays its own
        /// item); plain items top up plain stacks only. Currency and resources have no instance state and stack as usual.
        /// </summary>
        public List<(CharacterItemModel Item, bool IsNew)> Add(uint definitionId, uint count, ItemState state)
        {
            var changed = new List<(CharacterItemModel, bool)>();
            if (count == 0)
                return changed;

            if (definitionId == CurrencyItem)
            {
                CharacterItemModel purse = items.FirstOrDefault(i => i.Container == CurrencyContainer && i.ItemDefinitionId == CurrencyItem);
                if (purse != null)
                {
                    purse.Count = (uint)Math.Min((ulong)purse.Count + count, uint.MaxValue);
                    uint newCount = purse.Count;
                    ulong id = purse.Id;
                    Save(c => { var m = new CharacterItemModel { Id = id }; c.CharacterItem.Attach(m); m.Count = newCount; c.MarkModified(m, nameof(m.Count)); });
                    changed.Add((purse, false));
                }
                else
                    changed.Add((NewStack(CurrencyItem, count, CurrencyContainer, 0), true));
                return changed;
            }

            if (ResourceSlots.TryGetValue(definitionId, out (uint Slot, uint Max) resource))
            {
                // The resources tab holds up to its cap (InventoryDefinition SLOT_VALUE, 24,000,000); what doesn't fit waits
                // in overflow, as "Retrieve all" leaves it there (AbsorbResource). Before, the rest was dropped without a
                // word (a mail attachment, a vendor purchase or a builder's bag topping a stack past the cap), and a stack
                // already over the cap was cut down to it by any addition.
                CharacterItemModel stack = items.FirstOrDefault(i => i.Container == ResourceContainer && i.ItemDefinitionId == definitionId);
                uint excess;
                if (stack != null)
                {
                    uint room = stack.Count >= resource.Max ? 0 : resource.Max - stack.Count;
                    uint add = Math.Min(count, room);
                    excess = count - add;
                    if (add > 0)
                    {
                        stack.Count += add;
                        uint newCount = stack.Count;
                        ulong id = stack.Id;
                        Save(c => { var m = new CharacterItemModel { Id = id }; c.CharacterItem.Attach(m); m.Count = newCount; c.MarkModified(m, nameof(m.Count)); });
                        changed.Add((stack, false));
                    }
                }
                else
                {
                    uint add = Math.Min(count, resource.Max);
                    excess = count - add;
                    stack = NewStack(definitionId, add, ResourceContainer, resource.Slot);
                    changed.Add((stack, true));
                }
                if (excess > 0)
                {
                    uint maxResourceStack = Math.Max(1u, ItemDefinitionManager.Get(definitionId)?.MaxStack ?? 1u);
                    AddOverflow(definitionId, maxResourceStack, ref excess, changed, state);
                    if (excess > 0)
                        log.Warn($"Character {characterId}'s resources tab and overflow are full; dropped {excess}× item {definitionId}.");
                }
                return changed;
            }

            // Everything else tops up the bag's existing stacks of the item, then fills free bag slots.
            uint maxStack = Math.Max(1u, ItemDefinitionManager.Get(definitionId)?.MaxStack ?? 1u);
            uint left = count;
            foreach (CharacterItemModel stack in items.Where(i => i.Container == BagContainer
                && i.ItemDefinitionId == definitionId && i.Count < maxStack && state.IsPlain && IsPlain(i)).OrderBy(i => i.Slot))
            {
                uint add = Math.Min(left, maxStack - stack.Count);
                stack.Count += add;
                uint newCount = stack.Count;
                ulong id = stack.Id;
                Save(c => { var m = new CharacterItemModel { Id = id }; c.CharacterItem.Attach(m); m.Count = newCount; c.MarkModified(m, nameof(m.Count)); });
                changed.Add((stack, false));
                left -= add;
                if (left == 0)
                    return changed;
            }

            HashSet<uint> used = items.Where(i => i.Container == BagContainer).Select(i => i.Slot).ToHashSet();
            for (uint slot = 0; slot < BagSize && left > 0; slot++)
            {
                if (used.Contains(slot))
                    continue;
                uint add = Math.Min(left, maxStack);
                changed.Add((NewStack(definitionId, add, BagContainer, slot, state), true));
                left -= add;
            }
            // What doesn't fit waits in overflow: topping up its stacks of the item, then new overflow slots.
            AddOverflow(definitionId, maxStack, ref left, changed, state);
            if (left > 0)
                log.Warn($"Character {characterId}'s bag and overflow are full; dropped {left}× item {definitionId}.");
            return changed;
        }

        /// <summary>
        /// Puts items straight into overflow (the inventory's third tab), e.g. a lapsed site's chest contents (wiki
        /// Chests: the chest and its items go to the overflow inventory).
        /// </summary>
        public List<(CharacterItemModel Item, bool IsNew)> AddToOverflow(uint definitionId, uint count, ItemState state = default)
        {
            var changed = new List<(CharacterItemModel, bool)>();
            uint left = count;
            uint maxStack = Math.Max(1u, ItemDefinitionManager.Get(definitionId)?.MaxStack ?? 1u);
            AddOverflow(definitionId, maxStack, ref left, changed, state);
            if (left > 0)
                log.Warn($"Character {characterId}'s overflow is full; dropped {left}× item {definitionId}.");
            return changed;
        }

        private void AddOverflow(uint definitionId, uint maxStack, ref uint left, List<(CharacterItemModel, bool)> changed,
            ItemState state)
        {
            // While overflow items expire, an arrival tops up only a stack whose end is within SameArrivalWindow of its
            // own (one that arrived in the last minute): the client counts down the time it was sent with the item, so
            // topping up an older stack would hand new items its earlier end — by at most a minute this way. Until
            // 2026-10-18 nothing stacked while expiry was on, so a player with a full bag who kept looting or gathering
            // filled all MaxOverflowSlots with one-item stacks and later items were dropped (review 2026-10-18b #14).
            DateTime? arrivalEnd = NewOverflowExpiry();
            foreach (CharacterItemModel stack in items.Where(i => i.Container == OverflowContainer
                && (arrivalEnd == null || i.ExpiresAt is DateTime end && end <= arrivalEnd && arrivalEnd.Value - end <= SameArrivalWindow)
                && i.ItemDefinitionId == definitionId && i.Count < maxStack && state.IsPlain && IsPlain(i)).OrderBy(i => i.Slot))
            {
                if (left == 0)
                    break;
                uint add = Math.Min(left, maxStack - stack.Count);
                stack.Count += add;
                uint newCount = stack.Count;
                ulong id = stack.Id;
                Save(c => { var m = new CharacterItemModel { Id = id }; c.CharacterItem.Attach(m); m.Count = newCount; c.MarkModified(m, nameof(m.Count)); });
                changed.Add((stack, false));
                left -= add;
            }
            HashSet<uint> overflowUsed = items.Where(i => i.Container == OverflowContainer).Select(i => i.Slot).ToHashSet();
            for (uint slot = 0; slot < MaxOverflowSlots && left > 0; slot++)
            {
                if (overflowUsed.Contains(slot))
                    continue;
                uint add = Math.Min(left, maxStack);
                changed.Add((NewStack(definitionId, add, OverflowContainer, slot, state), true));
                left -= add;
            }
        }

        /// <summary>Does the character have items waiting in overflow?</summary>
        public bool HasOverflow => items.Any(i => i.Container == OverflowContainer);

        /// <summary>
        /// "Retrieve all": moves overflow items into the bag as far as they fit, topping up the bag's stacks of the same
        /// item first, then filling free bag slots. Resources go to the resources tab instead (patch notes 7 Jun 2016,
        /// P5789: "Resources from overflow now go directly into your resources tab when the 'retrieve all' button is
        /// clicked"), up to the tab's cap for that resource; the rest waits in overflow. Returns every stack that changed
        /// (Deleted = an overflow stack used up, Added = a new stack); a stack moved whole keeps its guid and only changes
        /// container and slot.
        /// </summary>
        public List<(CharacterItemModel Item, bool Deleted, bool Added)> AbsorbOverflow()
        {
            var changed = new List<(CharacterItemModel, bool, bool)>();
            foreach (CharacterItemModel waiting in items.Where(i => i.Container == OverflowContainer).OrderBy(i => i.Slot).ToList())
            {
                if (ResourceSlots.TryGetValue(waiting.ItemDefinitionId, out (uint Slot, uint Max) resource))
                {
                    AbsorbResource(waiting, resource.Slot, resource.Max, changed);
                    continue;
                }
                uint maxStack = Math.Max(1u, ItemDefinitionManager.Get(waiting.ItemDefinitionId)?.MaxStack ?? 1u);
                uint before = waiting.Count;
                foreach (CharacterItemModel stack in items.Where(i => i.Container == BagContainer
                    && i.ItemDefinitionId == waiting.ItemDefinitionId && i.Count < maxStack && IsPlain(waiting) && IsPlain(i))
                    .OrderBy(i => i.Slot).ToList())
                {
                    if (waiting.Count == 0)
                        break;
                    uint add = Math.Min(waiting.Count, maxStack - stack.Count);
                    stack.Count += add;
                    uint newCount = stack.Count;
                    ulong id = stack.Id;
                    Save(c => { var m = new CharacterItemModel { Id = id }; c.CharacterItem.Attach(m); m.Count = newCount; c.MarkModified(m, nameof(m.Count)); });
                    changed.Add((stack, false, false));
                    waiting.Count -= add;
                }
                ulong waitingId = waiting.Id;
                if (waiting.Count == 0)
                {
                    items.Remove(waiting);
                    Save(c => { var m = new CharacterItemModel { Id = waitingId }; c.CharacterItem.Attach(m); c.CharacterItem.Remove(m); });
                    changed.Add((waiting, true, false));
                    continue;
                }
                uint? free = FirstFreeBagSlot();
                if (free != null)
                {
                    SetLocation(waiting, BagContainer, free.Value);
                    changed.Add((waiting, false, false));
                }
                else if (waiting.Count != before)
                {
                    // Partly merged: save its smaller count.
                    uint newCount = waiting.Count;
                    Save(c => { var m = new CharacterItemModel { Id = waitingId }; c.CharacterItem.Attach(m); m.Count = newCount; c.MarkModified(m, nameof(m.Count)); });
                    changed.Add((waiting, false, false));
                }
            }
            return changed;
        }

        /// <summary>
        /// One overflow resource stack into the resources tab: tops up the tab's stack of it to the resource's cap, or
        /// moves the stack there whole (a new tab stack when it is over the cap); what doesn't fit stays in overflow.
        /// </summary>
        private void AbsorbResource(CharacterItemModel waiting, uint slot, uint max, List<(CharacterItemModel, bool, bool)> changed)
        {
            ulong waitingId = waiting.Id;
            uint before = waiting.Count;
            CharacterItemModel stack = items.FirstOrDefault(i => i.Container == ResourceContainer && i.ItemDefinitionId == waiting.ItemDefinitionId);
            if (stack == null && waiting.Count <= max)
            {
                SetLocation(waiting, ResourceContainer, slot);
                changed.Add((waiting, false, false));
                return;
            }
            if (stack == null)
            {
                changed.Add((NewStack(waiting.ItemDefinitionId, max, ResourceContainer, slot), false, true));
                waiting.Count -= max;
            }
            else if (stack.Count < max)
            {
                uint add = Math.Min(waiting.Count, max - stack.Count);
                stack.Count += add;
                uint stackCount = stack.Count;
                ulong stackId = stack.Id;
                Save(c => { var m = new CharacterItemModel { Id = stackId }; c.CharacterItem.Attach(m); m.Count = stackCount; c.MarkModified(m, nameof(m.Count)); });
                changed.Add((stack, false, false));
                waiting.Count -= add;
            }
            if (waiting.Count == 0)
            {
                items.Remove(waiting);
                Save(c => { var m = new CharacterItemModel { Id = waitingId }; c.CharacterItem.Attach(m); c.CharacterItem.Remove(m); });
                changed.Add((waiting, true, false));
            }
            else if (waiting.Count != before)
            {
                uint newCount = waiting.Count;
                Save(c => { var m = new CharacterItemModel { Id = waitingId }; c.CharacterItem.Attach(m); m.Count = newCount; c.MarkModified(m, nameof(m.Count)); });
                changed.Add((waiting, false, false));
            }
        }

        /// <summary>
        /// Removes up to <paramref name="count"/> of an item across stacks. Returns false (and changes nothing) if the
        /// character doesn't have enough.
        /// </summary>
        public bool TryRemove(uint definitionId, uint count, List<(CharacterItemModel Item, bool Deleted)> changed)
        {
            // Bag and storage stacks first, worn items last (a recipe that takes a tool should not take the one in hand
            // while a spare is in the bag: nothing tells the other players it went). Never a picked-up chest that still
            // holds items: its contents are keyed to its guid and were lost with it (crafting, placing a prop or using an
            // item takes "so many of this kind" and could pick it).
            List<CharacterItemModel> stacks = items.Where(i => i.ItemDefinitionId == definitionId && !Chests.HasItems(i.Id))
                .OrderBy(i => i.Container == EquippedContainer).ToList();
            if (stacks.Sum(s => (long)s.Count) < count)
                return false;

            uint left = count;
            foreach (CharacterItemModel stack in stacks)
            {
                if (left == 0)
                    break;
                uint take = Math.Min(left, stack.Count);
                stack.Count -= take;
                left -= take;

                ulong id = stack.Id;
                if (stack.Count == 0)
                {
                    items.Remove(stack);
                    Save(c => { var m = new CharacterItemModel { Id = id }; c.CharacterItem.Attach(m); c.CharacterItem.Remove(m); });
                    changed.Add((stack, true));
                }
                else
                {
                    uint newCount = stack.Count;
                    Save(c => { var m = new CharacterItemModel { Id = id }; c.CharacterItem.Attach(m); m.Count = newCount; c.MarkModified(m, nameof(m.Count)); });
                    changed.Add((stack, false));
                }
            }
            return true;
        }

        /// <summary>Takes up to <paramref name="count"/> from one stack (deleting it when empty).</summary>
        public (CharacterItemModel Item, bool Deleted)? RemoveFrom(ulong guid, uint count)
        {
            CharacterItemModel stack = Get(guid);
            if (stack == null || count == 0)
                return null;
            stack.Count -= Math.Min(count, stack.Count);
            ulong id = stack.Id;
            if (stack.Count == 0)
            {
                items.Remove(stack);
                Save(c => { var m = new CharacterItemModel { Id = id }; c.CharacterItem.Attach(m); c.CharacterItem.Remove(m); });
                return (stack, true);
            }
            uint newCount = stack.Count;
            Save(c => { var m = new CharacterItemModel { Id = id }; c.CharacterItem.Attach(m); m.Count = newCount; c.MarkModified(m, nameof(m.Count)); });
            return (stack, false);
        }

        public uint CountOf(uint definitionId) => (uint)items.Where(i => i.ItemDefinitionId == definitionId).Sum(i => (long)i.Count);

        public CharacterItemModel At(uint container, uint slot) => items.FirstOrDefault(i => i.Container == container && i.Slot == slot);
        public CharacterItemModel Get(ulong guid) => items.FirstOrDefault(i => i.Id == guid);

        private const uint EquippedInventoryType = 2;
        private static Dictionary<uint, uint> equipSlotToContainerSlot;

        /// <summary>
        /// Equipped-container slot for an equipment slot (InventoryDefinition type 2): main hand (2) → 2, feet → 7,
        /// grappling hook → 8, chest → 9...
        /// <para>
        /// Chest (equipment slot 8) has two rows: 325 → slot 9 and 328 → slot 1 (PARAM2 1, SORT_ORDER 4 before 9's 5).
        /// The 0.695 inventory window labels slot 1 "Appearance Outfit - Replaces the appearance of your armor" and slot 9
        /// "Armor" (re/as3_695 InventoryDocumentClass.as 253-261) [P], and the outfit's own row (4, slot 1) has PARAM2 1
        /// too, so PARAM2 1 marks the appearance slot [I]. An item's own slot is therefore the PARAM2 0 row first:
        /// picking by SORT_ORDER alone put chest armour into the outfit slot and took the outfit off (OPEN-LEADS L138,
        /// trace/0695/agent-l138-a.md). Slot 1 stays allowed for a chest piece (Fits) when the client names it.
        /// </para>
        /// </summary>
        public static uint? EquippedSlotFor(uint equipSlotId)
        {
            equipSlotToContainerSlot ??= GameTableManager.Instance.InventoryDefinitions.Entries
                .Where(e => e.InventoryType == EquippedInventoryType)
                .GroupBy(e => e.SlotId)
                .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Param2 == 0 ? 0 : 1).ThenBy(e => e.SortOrder).First().SlotNumber);
            return equipSlotToContainerSlot.TryGetValue(equipSlotId, out uint slot) ? slot : null;
        }

        /// <summary>
        /// The lowest empty bag slot, or null when the bag is full.
        /// </summary>
        /// <summary>One item in its own bag slot, never stacked with others (a picked-up chest that holds items).</summary>
        public CharacterItemModel AddSingle(uint definitionId)
        {
            uint? slot = FirstFreeBagSlot();
            return slot == null ? null : NewStack(definitionId, 1, BagContainer, slot.Value);
        }

        /// <summary>Empty bag slots.</summary>
        /// <summary>
        /// How many of an item still fit in the bag: room on its part-full plain stacks plus the free slots. Items that
        /// come with an instance state (<paramref name="plain"/> false: a store purchase's waiting period) only fill free
        /// slots (<see cref="Add(uint, uint, ItemState)"/> never tops up a stack with them).
        /// </summary>
        public long SpaceFor(uint definitionId, bool plain = true)
        {
            uint maxStack = Math.Max(1u, ItemDefinitionManager.Get(definitionId)?.MaxStack ?? 1u);
            long partial = !plain ? 0 : items.Where(i => i.Container == BagContainer && i.ItemDefinitionId == definitionId
                    && i.Count < maxStack && IsPlain(i))
                .Sum(i => (long)(maxStack - i.Count));
            return partial + (long)FreeBagSlots() * maxStack;
        }

        /// <summary>
        /// Would these incoming stacks fit in the bag once the outgoing offers (guid → count) have left? Incoming items
        /// first fill this bag's part-full stacks of the same item (as <see cref="Add"/> does; stacks being given away
        /// don't count), the rest needs free slots; a stack given away whole frees its slot. Player Design items never
        /// stack. Used by trades (the client's "TradeCannotAcceptFullInventory").
        /// </summary>
        public bool CanReceive(IEnumerable<(uint Definition, uint Count)> incoming, IReadOnlyDictionary<ulong, uint> outgoing)
        {
            int free = FreeBagSlots() + outgoing.Count(o => Get(o.Key) is { } i && i.Container == BagContainer && o.Value >= i.Count);
            long needed = 0;
            foreach (var group in incoming.GroupBy(x => x.Definition))
            {
                if (group.Key == Game.DesignItems.PlayerDesignItem)
                {
                    needed += group.Count();
                    continue;
                }
                long maxStack = Math.Max(1u, ItemDefinitionManager.Get(group.Key)?.MaxStack ?? 1u);
                long count = group.Sum(x => (long)x.Count);
                long partial = items.Where(i => i.Container == BagContainer && i.ItemDefinitionId == group.Key
                        && i.Count < maxStack && !outgoing.ContainsKey(i.Id))
                    .Sum(i => maxStack - i.Count);
                needed += (Math.Max(0, count - partial) + maxStack - 1) / maxStack;
            }
            return needed <= free;
        }

        /// <summary>Grows the bag (an Inventory Expansion was used); the caller tells the client.</summary>
        public void SetBagSize(uint size) => BagSize = size;

        public int FreeBagSlots() => (int)BagSize - items.Count(i => i.Container == BagContainer);

        private uint? FirstFreeBagSlot()
        {
            HashSet<uint> used = items.Where(i => i.Container == BagContainer).Select(i => i.Slot).ToHashSet();
            for (uint slot = 0; slot < BagSize; slot++)
                if (!used.Contains(slot))
                    return slot;
            return null;
        }

        /// <summary>
        /// A container's sort (Inventory / Chest / Vault window "sort by" dropdown → ClientInventory.SetItemsToSlots →
        /// RearrangeItemSlots): the listed items of one container (bag or vault, <paramref name="capacity"/> slots) go to
        /// the listed slots. Refused (nothing changes)
        /// when an item isn't in that container, a slot is out of range or used twice. Unlisted items sitting in a
        /// listed slot move to a free one. Returns the items whose slot changed.
        /// </summary>
        public List<CharacterItemModel> Rearrange(uint container, IReadOnlyList<(uint Slot, ulong Guid)> order, uint capacity)
        {
            var changed = new List<CharacterItemModel>();
            if (capacity == 0)
                return changed;
            var plan = new List<(CharacterItemModel Item, uint Slot)>();
            var slots = new HashSet<uint>();
            foreach ((uint slot, ulong guid) in order)
            {
                CharacterItemModel item = Get(guid);
                if (item == null || item.Container != container || slot >= capacity || !slots.Add(slot))
                    return changed;
                plan.Add((item, slot));
            }
            var listed = plan.Select(p => p.Item).ToHashSet();
            var occupied = new HashSet<uint>(slots);
            foreach (CharacterItemModel other in items.Where(i => i.Container == container && !listed.Contains(i)))
                occupied.Add(other.Slot);
            foreach (CharacterItemModel other in items.Where(i => i.Container == container && !listed.Contains(i) && slots.Contains(i.Slot)).ToList())
            {
                uint? free = Enumerable.Range(0, (int)capacity).Select(s => (uint?)s).FirstOrDefault(s => !occupied.Contains(s.Value));
                if (free == null)
                    return changed;
                occupied.Add(free.Value);
                SetLocation(other, container, free.Value);
                changed.Add(other);
            }
            foreach ((CharacterItemModel item, uint slot) in plan.Where(p => p.Item.Slot != p.Slot))
            {
                SetLocation(item, container, slot);
                changed.Add(item);
            }
            return changed;
        }

        /// <summary>The vault's container id (ItemUses.VaultContainer).</summary>
        public const uint VaultContainer = 1;

        /// <summary>How many slots a container has for this character: the bag, the vault; 0 for anything else.</summary>
        public uint CapacityOf(uint container)
            => container == BagContainer ? BagSize : container == VaultContainer ? Game.ItemUses.VaultSizeOf(characterId) : 0;

        private uint? FirstFree(uint container)
        {
            uint size = CapacityOf(container);
            for (uint slot = 0; slot < size; slot++)
                if (At(container, slot) == null)
                    return slot;
            return null;
        }

        /// <summary>
        /// Part of a stack dragged somewhere (inventory split dialog on Shift-drag, one item on Ctrl-drag), or a whole
        /// stack dropped on a matching one: onto an empty bag / vault slot it becomes a new stack there, onto the same
        /// item it is added up to the item's stack limit. Anything else is refused (empty result). Returns the stacks
        /// that changed and whether each was emptied (deleted).
        /// </summary>
        public List<(CharacterItemModel Item, bool Deleted)> MoveCount(CharacterItemModel item, uint container, uint slot, uint count)
        {
            var changed = new List<(CharacterItemModel, bool)>();
            uint size = CapacityOf(container);
            if (size == 0 || count == 0 || (item.Container != BagContainer && item.Container != VaultContainer))
                return changed;
            count = Math.Min(count, item.Count);
            uint maxStack = Math.Max(1u, ItemDefinitionManager.Get(item.ItemDefinitionId)?.MaxStack ?? 1u);
            if (slot == uint.MaxValue)
                slot = items.Where(i => i.Container == container && i != item && i.ItemDefinitionId == item.ItemDefinitionId && i.Count < maxStack)
                           .OrderBy(i => i.Slot).Select(i => (uint?)i.Slot).FirstOrDefault()
                       ?? FirstFree(container) ?? uint.MaxValue;
            if (slot >= size)
                return changed;
            CharacterItemModel target = At(container, slot);
            if (target == item)
                return changed;
            if (target != null)
            {
                if (target.ItemDefinitionId != item.ItemDefinitionId || target.Count >= maxStack)
                    return changed;
                uint moved = Math.Min(count, maxStack - target.Count);
                SetCount(target, target.Count + moved);
                // Store items still waiting to become tradeable lock the stack they join until the later time.
                if (IsTradeLocked(item) && (target.TradeableAfter ?? DateTime.MinValue) < item.TradeableAfter)
                    SetTradeableAfter(target, item.TradeableAfter);
                changed.Add((target, false));
                if (RemoveFrom(item.Id, moved) is { } source)
                    changed.Add(source);
                return changed;
            }
            if (count == item.Count)
            {
                SetLocation(item, container, slot);
                changed.Add((item, false));
                return changed;
            }
            ItemState state = StateOf(item);   // a split part keeps the stack's state (a store item's waiting period)
            if (RemoveFrom(item.Id, count) is { } rest)
                changed.Add(rest);
            changed.Add((NewStack(item.ItemDefinitionId, count, container, slot, state), false));
            return changed;
        }

        private void SetTradeableAfter(CharacterItemModel item, DateTime? when)
        {
            item.TradeableAfter = when;
            ulong id = item.Id;
            Save(c => { var m = new CharacterItemModel { Id = id }; c.CharacterItem.Attach(m); m.TradeableAfter = when; c.MarkModified(m, nameof(m.TradeableAfter)); });
        }

        private void SetCount(CharacterItemModel item, uint count)
        {
            item.Count = count;
            ulong id = item.Id;
            Save(c => { var m = new CharacterItemModel { Id = id }; c.CharacterItem.Attach(m); m.Count = count; c.MarkModified(m, nameof(m.Count)); });
        }

        /// <summary>
        /// Moves an item, swapping with whatever is at the destination. Returns the items whose location changed.
        /// </summary>
        public List<CharacterItemModel> Move(CharacterItemModel item, uint container, uint slot)
        {
            var moved = new List<CharacterItemModel>();
            if (item.Container == container && item.Slot == slot)
                return moved;

            CharacterItemModel occupant = At(container, slot);
            if (occupant != null)
            {
                // The occupant swaps into the item's old place, unless that place is the overflow tab, the resources tab
                // or the purse: there it would wait out the overflow timer and be deleted (an item taken out of overflow
                // onto a full bag slot, or equipped straight from overflow, sent the bag item / worn item into overflow),
                // or sit in a resource slot or the purse, where nothing expects a bag item. It goes to a free bag slot
                // instead; with none, nothing moves.
                bool swapBack = item.Container is not (OverflowContainer or ResourceContainer or CurrencyContainer);
                uint? elsewhere = swapBack ? null : FirstFreeBagSlot();
                if (!swapBack && elsewhere == null)
                    return moved;
                if (swapBack)
                    SetLocation(occupant, item.Container, item.Slot);
                else
                    SetLocation(occupant, BagContainer, elsewhere.Value);
                moved.Add(occupant);
            }
            SetLocation(item, container, slot);
            moved.Add(item);
            return moved;
        }

        /// <summary>
        /// A move requested by the client, checked: into the bag (any slot = first free) or into an equipment slot
        /// that accepts the item. Anything else is refused (empty result).
        /// </summary>
        public List<CharacterItemModel> MoveTo(CharacterItemModel item, uint container, uint slot)
        {
            LastRefusal = 0;
            switch (container)
            {
                case BagContainer:
                    if (slot == uint.MaxValue)
                    {
                        if (item.Container == BagContainer)
                            return new();
                        uint? free = FirstFreeBagSlot();
                        if (free == null)
                            return new();
                        slot = free.Value;
                    }
                    if (slot >= BagSize)
                        return new();
                    // A swap would send the occupant to the item's old place, which must suit it too.
                    // Unequipping (e.g. a right-click on worn gear names bag slot 0) onto an item that can't go to the
                    // gear slot in exchange lands in the first free bag slot instead.
                    CharacterItemModel occupant = At(container, slot);
                    if (occupant != null && item.Container == EquippedContainer
                        && (!Fits(occupant, item.Slot) || DuplicatesUnique(occupant, item.Slot)))
                    {
                        uint? freeSlot = FirstFreeBagSlot();
                        if (freeSlot == null)
                            return new();
                        slot = freeSlot.Value;
                    }
                    return Move(item, container, slot);
                case VaultContainer:
                {
                    // The vault (only gear-free moves: worn items go to the bag first).
                    if (item.Container == EquippedContainer)
                        return new();
                    uint size = CapacityOf(VaultContainer);
                    if (slot == uint.MaxValue)
                    {
                        if (item.Container == VaultContainer)
                            return new();
                        uint? freeVault = FirstFree(VaultContainer);
                        if (freeVault == null)
                            return new();
                        slot = freeVault.Value;
                    }
                    if (slot >= size)
                        return new();
                    return Move(item, container, slot);
                }
                case EquippedContainer:
                    if (!Fits(item, slot))
                    {
                        // A right-click "equip" can name a gear slot the item doesn't belong in (boots arrive with
                        // the main hand's slot); use the item's own slot then.
                        ItemDefinitionManager.ItemDefinition definition = ItemDefinitionManager.Get(item.ItemDefinitionId);
                        uint? own = definition == null || definition.EquipSlotId == 0 ? null : EquippedSlotFor(definition.EquipSlotId);
                        if (own == null || !Fits(item, own.Value))
                            return new();
                        slot = own.Value;
                    }
                    if (DuplicatesUnique(item, slot))
                    {
                        LastRefusal = UniqueEquipRefusal;
                        return new();
                    }
                    return Move(item, container, slot);
                default:
                    return new();
            }
        }

        private static HashSet<(uint EquipSlotId, uint SlotNumber)> equippedSlots;

        /// <summary>
        /// True if the item may sit in that equipped-container slot. Some equipment slots have two places (the two
        /// accessory slots), so this checks every InventoryDefinition row, not just the preferred one.
        /// </summary>
        private static bool Fits(CharacterItemModel item, uint equippedSlot)
        {
            equippedSlots ??= GameTableManager.Instance.InventoryDefinitions.Entries
                .Where(e => e.InventoryType == EquippedInventoryType)
                .Select(e => (e.SlotId, e.SlotNumber))
                .ToHashSet();
            ItemDefinitionManager.ItemDefinition definition = ItemDefinitionManager.Get(item.ItemDefinitionId);
            return definition != null && definition.EquipSlotId != 0 && equippedSlots.Contains((definition.EquipSlotId, equippedSlot));
        }

        /// <summary>
        /// Equips a bag item into its equipment slot, or puts an equipped item back in the bag. This is what the
        /// client's "use" (a transfer onto the item's own location, e.g. a double-click) means for gear.
        /// </summary>
        public List<CharacterItemModel> ToggleEquip(CharacterItemModel item)
        {
            LastRefusal = 0;
            if (item.Container == EquippedContainer)
            {
                uint? free = FirstFreeBagSlot();
                return free == null ? new() : Move(item, BagContainer, free.Value);
            }

            ItemDefinitionManager.ItemDefinition definition = ItemDefinitionManager.Get(item.ItemDefinitionId);
            uint? equippedSlot = definition == null || definition.EquipSlotId == 0 ? null : EquippedSlotFor(definition.EquipSlotId);
            if (equippedSlot == null)
                return new();
            if (DuplicatesUnique(item, equippedSlot.Value))
            {
                LastRefusal = UniqueEquipRefusal;
                return new();
            }
            return Move(item, EquippedContainer, equippedSlot.Value);
        }

        /// <summary>
        /// TransferItemResponse reason 0x22: the client opens its unique-equip dialog (FUN_142f1eb90, called from the
        /// TransferItemResponse handler FUN_142f202e0; title Equip.UniqueEquipWarningTitle, text Equip.UniqueEquip 562352
        /// "Only a single instance of this type of item may be equipped at a time.").
        /// </summary>
        public const uint UniqueEquipRefusal = 0x22;

        /// <summary>
        /// Why the last <see cref="MoveTo"/> / <see cref="ToggleEquip"/> refused, as a TransferItemResponse reason
        /// (<see cref="UniqueEquipRefusal"/>), or 0 when it moved or was refused for a reason with no client message.
        /// </summary>
        public uint LastRefusal { get; internal set; }

        /// <summary>
        /// True if putting <paramref name="item"/> into equipped slot <paramref name="equippedSlot"/> would wear two
        /// copies of a UniqueEquip item (<see cref="ItemDefinitionManager.ItemDefinition.UniqueEquip"/>: every
        /// accessory). Live rule P4840 (26 Apr 2016): "Multiple copies of the Ring of Bounty can no longer be equipped
        /// at the same time" (TASKS 6.54). The item being replaced in that slot and the item itself (moving between the
        /// two accessory slots) don't count; any other equipped item with the same definition does. Server-side only:
        /// the client sends the transfer and shows the refusal from reason 0x22.
        /// </summary>
        public bool DuplicatesUnique(CharacterItemModel item, uint equippedSlot)
        {
            if (ItemDefinitionManager.Get(item.ItemDefinitionId) is not { UniqueEquip: true })
                return false;
            return items.Any(other => other.Id != item.Id && other.Container == EquippedContainer && other.Slot != equippedSlot
                && other.ItemDefinitionId == item.ItemDefinitionId);
        }

        /// <summary>
        /// Saves an item's wear (Durability: worn by damage or harvesting, reset by a repair). Wear used to live only in
        /// memory, so a relog repaired everything; it is now saved with the item row like its count.
        /// </summary>
        public void SaveWear(CharacterItemModel item)
        {
            ulong id = item.Id;
            float wear = item.Wear;
            Save(c => { var m = new CharacterItemModel { Id = id }; c.CharacterItem.Attach(m); m.Wear = wear; c.MarkModified(m, nameof(m.Wear)); });
        }

        /// <summary>Setting ItemAutoResupply: items keep their auto-resupply switch (default on = live).</summary>
        public static bool AutoResupplyKept => ConfigurationManager<WorldServerConfiguration>.Instance.Config?.ItemAutoResupply ?? true;

        /// <summary>
        /// Sets an item's auto-resupply switch and saves it (Inventory 0x2D sub 0x0D InventoryPacketToggleAutoResupply,
        /// InventoryHandler). The client flips its own copy (ClientItem +0xdc) before it sends the toggle and reads no
        /// reply (the S→C Inventory dispatch has no case 0x0D) [P], so nothing is sent back; the item records the server
        /// sends later (login, ItemAdd, chest lists, trade) carry the saved value (<see cref="ToPacket"/>). ItemUpdate
        /// (ClientUpdate 0x0F) has no such field. False when the setting is off or nothing changes.
        /// </summary>
        public bool SetAutoResupply(CharacterItemModel item, bool on)
        {
            if (!AutoResupplyKept || item == null || item.AutoResupply == on)
                return false;
            item.AutoResupply = on;
            ulong id = item.Id;
            Save(c => { var m = new CharacterItemModel { Id = id }; c.CharacterItem.Attach(m); m.AutoResupply = on; c.MarkModified(m, nameof(m.AutoResupply)); });
            return true;
        }

        /// <summary>The instance state an item keeps when it is handed over or split (see <see cref="ItemState"/>).</summary>
        public static ItemState StateOf(CharacterItemModel item) => new(item.Wear, item.TradeableAfter, item.AutoResupply);

        /// <summary>
        /// Is a store purchase still in its waiting period (the client's "fraud timer": TransferItemResponse 0x12 "Item
        /// can't be traded if it has fraud timer on it"; texts 577061 / 562759)? Trades, mail and chests refuse it.
        /// </summary>
        public static bool IsTradeLocked(CharacterItemModel item) => item.TradeableAfter is DateTime t && t > DateTime.UtcNow;

        /// <summary>Has the stack no instance state, so other items of its kind may join it?</summary>
        private static bool IsPlain(CharacterItemModel item) => StateOf(item).IsPlain;

        /// <summary>
        /// Moves an item to a container slot in memory and saves the new location.
        /// </summary>
        private void SetLocation(CharacterItemModel item, uint container, uint slot)
        {
            item.Container = container;
            item.Slot = slot;
            ulong id = item.Id;
            Save(c => { var m = new CharacterItemModel { Id = id }; c.CharacterItem.Attach(m); m.Container = container; m.Slot = slot; c.MarkModified(m, nameof(m.Container), nameof(m.Slot)); });
            // Into overflow starts its time; out of it ends it.
            if (container == OverflowContainer && item.ExpiresAt == null && OverflowExpiryHours > 0)
                SetExpiresAt(item, NewOverflowExpiry());
            else if (container != OverflowContainer && item.ExpiresAt != null)
                SetExpiresAt(item, null);
        }

        /// <summary>
        /// Creates and saves a new item stack, with the instance state it brings (<see cref="ItemState"/>).
        /// </summary>
        private CharacterItemModel NewStack(uint definitionId, uint count, uint container, uint slot, ItemState state = default)
        {
            var stack = new CharacterItemModel
            {
                Id = NewItemGuid(),
                CharacterId = characterId,
                ItemDefinitionId = definitionId,
                Count = count,
                Container = container,
                Slot = slot,
                Wear = state.Wear,
                TradeableAfter = state.TradeableAfter,
                AutoResupply = state.AutoResupply && AutoResupplyKept,
                ExpiresAt = container == OverflowContainer ? NewOverflowExpiry() : null
            };
            items.Add(stack);
            var copy = new CharacterItemModel
            {
                Id = stack.Id, CharacterId = characterId, ItemDefinitionId = definitionId,
                Count = count, Container = container, Slot = slot, Wear = stack.Wear, TradeableAfter = stack.TradeableAfter,
                AutoResupply = stack.AutoResupply, ExpiresAt = stack.ExpiresAt
            };
            Save(c => c.CharacterItem.Add(copy));
            return stack;
        }

        // Saves run in the background but in order, so an update never reaches the database before its insert.
        private System.Threading.Tasks.Task saveChain = System.Threading.Tasks.Task.CompletedTask;
        private readonly object saveLock = new();

        /// <summary>
        /// Queues a database change. Changes for one character run one after another, so later saves can't overtake earlier
        /// ones; failures are logged.
        /// </summary>
        private void Save(Action<Database.Character.CharacterContext> action)
        {
            lock (saveLock)
            {
                saveChain = saveChain.ContinueWith(_ => DatabaseManager.Instance.CharacterDatabase.Save(action)).Unwrap()
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            log.Error(t.Exception, "Failed to save inventory change.");
                    });
            }
        }

        /// <summary>
        /// The client's view of an item, laid out like the live game's (see the SendSelf capture).
        /// </summary>
        public static InventoryItem ToPacket(CharacterItemModel item)
        {
            var packet = new InventoryItem
            {
                Guid = item.Id,
                Count = item.Count,
                ContainerId = item.Container,
                SlotId = item.Slot,
                OwnerType = 1,   // the player
                // ClientItem +0xdc, the auto-resupply switch the player last sent (Inventory 0x2D sub 0x0D).
                AutoResupply = item.AutoResupply && AutoResupplyKept,
                Wear = BitConverter.SingleToUInt32Bits(item.Wear),
                SecondsSinceLastCast = int.MaxValue,   // never used
                // An overflow item's seconds left: the client counts it down ("Overflow: Items expire in H:MM:SS").
                TimedDeleteSeconds = item.Container == OverflowContainer && item.ExpiresAt is DateTime end
                    ? (uint)Math.Clamp(Math.Ceiling((end - DateTime.UtcNow).TotalSeconds), 0, int.MaxValue) : 0
            };
            FillDefinition(packet, item.ItemDefinitionId);
            if (item.ItemDefinitionId == DesignItems.PlayerDesignItem)
                packet.Design = DesignItems.ItemBlock(item.Id);
            return packet;
        }

        /// <summary>Dev switch (itemstats on|off): tool stats in item records.</summary>
        public static bool SendItemStats = true;

        /// <summary>RefItemQuality.txt COST_MULTIPLIER by quality (0-3 → 2, 3, 4, 5).</summary>
        private static Dictionary<uint, float> costMultipliers;

        /// <summary>
        /// The definition header of an item record as the live server sent it: definition id, the definition's
        /// quality (the tooltip's name colour and category icon) and that quality's cost multiplier.
        /// </summary>
        public static void FillDefinition(InventoryItem packet, uint definitionId)
        {
            costMultipliers ??= GameDataFiles.Rows("RefItemQuality.txt").Where(r => r.Length > 4)
                .ToDictionary(r => uint.Parse(r[0]), r => float.Parse(r[4], System.Globalization.CultureInfo.InvariantCulture));
            uint quality = ItemDefinitionManager.Get(definitionId)?.Quality ?? 0;
            packet.Definition.DefinitionId = definitionId;
            packet.Definition.Random.Quality = quality;
            packet.Definition.CostMultiplier = BitConverter.SingleToUInt32Bits(costMultipliers.GetValueOrDefault(quality));
            // Gathering tools carry their stats (ItemCategoryStatMappings.txt: picks and axes have 3, 13, 12), laid out
            // as the live server sent them: {stat, text -1, value, 1, true, value, 1, 1}.
            if (SendItemStats && Abilities.ToolDisplayStats(definitionId) is var (area, speed, damage))
                foreach ((uint stat, float value) in new[] { (3u, area), (13u, speed), (12u, damage) })
                    packet.Definition.Random.Stats.Add(new InventoryItem.DefinitionData.RandomData.StatInfo
                    {
                        StatId = stat,
                        NameText = new LandmarkEmulator.Shared.Game.LandmarkText(uint.MaxValue),
                        Value = BitConverter.SingleToUInt32Bits(value),
                        Multiplier = BitConverter.SingleToUInt32Bits(1f),
                        Flag = true,
                        CurrentValue = BitConverter.SingleToUInt32Bits(value),
                        Multiplier2 = BitConverter.SingleToUInt32Bits(1f),
                        Multiplier3 = BitConverter.SingleToUInt32Bits(1f)
                    });
        }
    }
}
