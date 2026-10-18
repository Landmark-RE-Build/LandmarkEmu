using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.WorldServer.Storage;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// A build site's inventory (OPEN-LEADS L146; research trace/0695/agent-e-leads.md §5): the items of the props placed
    /// on it, plus items added to the site without being placed. Live kept items on a claim [P]:
    /// <list type="bullet">
    /// <item>the Claims 0xF9 registry names 0x0D SendInventoryDetails, 0x23 ClaimRequestMoveItemToPlayer, 0x47
    /// ClaimMoveItemToPlayerOnZone, 0x0E ClaimConfirmMoveItemToPlayer, 0x36 / 0x37 InventoryListDebugRequest / Response
    /// and 0x60 DebugAddItemClaim (0.589: 0x5E);</item>
    /// <item>the client's /item usage text: "listClaimInventory &lt;claim guid&gt; Show the items listed in a claims
    /// inventory", "claimItemToInventory &lt;item guid&gt; &lt;claim guid&gt; Requests a transfer of an item in a claim's
    /// inventory to the player's inventory", "claimAddItem &lt;item definition id&gt; &lt;quantity&gt; &lt;tint id&gt;
    /// &lt;claim guid&gt; [targetGuid]";</item>
    /// <item>the 0x37 printout lists each item with its <c>propGuid</c> and an owner type — a claim item is a prop placed
    /// as that item;</item>
    /// <item>patch notes P0037 (props missing from claims refunded), P0079 (7 Feb 2014: a claim can only be deleted
    /// with bag room for all its items and the flag), P0862 (lost claim items
    /// restored to the Overflow Inventory), P0165 (chests store items on a claim).</item>
    /// </list>
    /// So the inventory here is, for one site: every prop inside its box that was placed from a bag item
    /// (<see cref="Props.PlacedProp.PaidByItem"/>; a mover pack counts once, as its spawner item), and the unplaced items
    /// in <see cref="ClaimModel.Inventory"/>. Props placed with recipe materials from the palette are not items and are not
    /// listed. Only the admin commands reach it (the client's /item claimAddItem and claimItemToInventory); players see
    /// their props, not a list.
    /// </summary>
    public static class ClaimInventory
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>ClaimsBase subs (0.695 numbering).</summary>
        public const byte SendInventoryDetailsSub = 0x0D, MoveItemToPlayerSub = 0x23, DebugAddItemSub = 0x60;

        /// <summary>Largest count one claimAddItem may add [I: a guard, the client sends what the admin typed].</summary>
        private const uint MaxAddCount = 10_000;

        /// <summary>One item of a site's inventory: placed (its prop's guid) or not (prop guid 0).</summary>
        public sealed record Entry(ulong ItemGuid, uint Item, uint Tint, uint Count, ulong PropGuid, ulong Owner);

        /// <summary>A site's box in local metres (the site keeps world decimetres).</summary>
        private static (Vector3 Min, Vector3 Max) LocalBox(ClaimModel claim)
        {
            var origin = Terrain.TerrainStore.ZoneOrigin;
            return (new Vector3((float)((claim.MinX - origin.X) / 10), (float)((claim.MinY - origin.Y) / 10), (float)((claim.MinZ - origin.Z) / 10)),
                new Vector3((float)((claim.MaxX - origin.X) / 10), (float)((claim.MaxY - origin.Y) / 10), (float)((claim.MaxZ - origin.Z) / 10)));
        }

        /// <summary>
        /// The site's placed items: each prop in its box placed from a bag item, as (item guid = the bag item's guid it
        /// was placed from, or the prop's own guid when none was kept; the item; count 1; the prop). A mover pack's parts
        /// share one item (its spawner): only its first part is listed.
        /// </summary>
        private static List<(Entry Entry, Props.PlacedProp Prop)> Placed(ClaimModel claim)
        {
            (Vector3 min, Vector3 max) = LocalBox(claim);
            var seenPacks = new HashSet<ulong>();
            var list = new List<(Entry, Props.PlacedProp)>();
            foreach (Props.PlacedProp p in Props.PlacedInBox(claim.ZoneId, min, max).OrderBy(p => p.Spawn.Guid))
            {
                if (p.PaidByItem == 0 || (p.PackGroup != 0 && !seenPacks.Add(p.PackGroup)))
                    continue;
                ulong itemGuid = p.PaidByItemGuid != 0 ? p.PaidByItemGuid : p.Spawn.Guid;
                list.Add((new Entry(itemGuid, p.PaidByItem, 0, 1, p.Spawn.Guid, p.OwnerGuid), p));
            }
            return list;
        }

        /// <summary>The unplaced items of <see cref="ClaimModel.Inventory"/> ("guid:item:tint:count;…").</summary>
        private static List<Entry> Unplaced(ClaimModel claim)
        {
            var list = new List<Entry>();
            foreach (string part in (claim.Inventory ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = part.Split(':');
                if (f.Length == 4 && ulong.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out ulong guid)
                    && uint.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out uint item)
                    && uint.TryParse(f[2], NumberStyles.None, CultureInfo.InvariantCulture, out uint tint)
                    && uint.TryParse(f[3], NumberStyles.None, CultureInfo.InvariantCulture, out uint count))
                    list.Add(new Entry(guid, item, tint, count, 0, claim.OwnerGuid));
            }
            return list;
        }

        /// <summary>Stores the unplaced items (memory and database).</summary>
        private static void SaveUnplaced(ClaimModel claim, List<Entry> items)
        {
            string text = string.Join(";", items.Select(e => string.Create(CultureInfo.InvariantCulture, $"{e.ItemGuid}:{e.Item}:{e.Tint}:{e.Count}")));
            if (text.Length > 4096)
                throw new InvalidOperationException("claim inventory text too long");
            claim.Inventory = text;
            ulong id = claim.Id;
            DatabaseManager.Instance.CharacterDatabase.SaveWorld(c =>
            {
                var model = new ClaimModel { Id = id };
                c.Claim.Attach(model);
                model.Inventory = text;
                c.MarkModified(model, nameof(model.Inventory));
            }).ContinueWith(t => log.Error(t.Exception, $"Failed to save site {id}'s inventory."),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>Every item of a site's inventory: placed ones first, then the unplaced.</summary>
        public static List<Entry> Of(ClaimModel claim)
            => Placed(claim).Select(x => x.Entry).Concat(Unplaced(claim)).ToList();

        /// <summary>
        /// DebugAddItemClaim (ClaimsBase 0x60; the client's "/item claimAddItem &lt;item&gt; &lt;quantity&gt; &lt;tint&gt;
        /// &lt;claim guid&gt; [targetGuid]", builder FUN_1433c73a0, writer FUN_143380850) [P layout]: {u32 item definition,
        /// u32 tint, u32 count (the client makes it at least 1), u64 target (the admin target or the given guid), u64
        /// claim, u32 0, u32 0, u8 0}. The client echoes "Giving %d of itemId = %d, tintId = %d to target Guid %lld" itself.
        /// Admins only: adds the items to the site's unplaced list and answers with the site's inventory (0x0D).
        /// </summary>
        public static void HandleDebugAdd(WorldSession session, byte[] body)
        {
            if (!Admin.IsAdmin(session))
            {
                log.Info($"{session.CharacterName} (not an admin) sent DebugAddItemClaim; ignored.");
                return;
            }
            if (body.Length < 28)
                return;
            var reader = new GamePacketReader(body);
            uint item = reader.ReadUInt(), tint = reader.ReadUInt(), count = reader.ReadUInt();
            ulong target = reader.ReadULong(), claimId = reader.ReadULong();
            ClaimModel claim = Claims.Get(claimId);
            if (claim == null || Inventory.ItemDefinitionManager.Get(item) == null)
            {
                Chat.System(session, claim == null ? $"No build site {claimId}." : $"No item definition {item}.");
                return;
            }
            count = Math.Clamp(count, 1u, MaxAddCount);
            List<Entry> unplaced = Unplaced(claim);
            unplaced.Add(new Entry(Inventory.Inventory.NewItemGuid(), item, tint, count, 0, claim.OwnerGuid));
            try
            {
                SaveUnplaced(claim, unplaced);
            }
            catch (InvalidOperationException)
            {
                Chat.System(session, $"Build site {claimId}'s inventory is full.");
                return;
            }
            log.Info($"{session.CharacterName} added {count} x item {item} (tint {tint}, target {target}) to site {claimId}'s inventory.");
            SendDetails(session, claim);
        }

        /// <summary>
        /// ClaimRequestMoveItemToPlayer (ClaimsBase 0x23, client → server; the client's "/item claimItemToInventory
        /// &lt;item guid&gt; &lt;claim guid&gt;", built in FUN_1433acb90) [P layout]: {u64 claim guid, u64 player (the
        /// optional third argument, else the admin target, else the sender), u64 item guid}. (The client's own echo "Move
        /// and item with guid %llu to player inventory from claim Guid %llu" prints the claim and the player.) Admins only:
        /// an unplaced item goes to the player's bag (overflow when full, as P0862) and leaves the list; a placed one is
        /// picked up for that player as its owner's pick-up would (<see cref="Props.TakeAsClaimItem"/>). The player must
        /// be online. Answered with the site's inventory (0x0D). Live's own route went through the claim server and the
        /// zone (0x47 ClaimMoveItemToPlayerOnZone / 0x0E ClaimConfirmMoveItemToPlayer, no client classes) — one process
        /// here, so nothing is relayed.
        /// </summary>
        public static void HandleMoveToPlayer(WorldSession session, byte[] body)
        {
            if (!Admin.IsAdmin(session))
            {
                log.Info($"{session.CharacterName} (not an admin) sent ClaimRequestMoveItemToPlayer; ignored.");
                return;
            }
            if (body.Length < 24)
                return;
            var reader = new GamePacketReader(body);
            ulong claimId = reader.ReadULong(), playerGuid = reader.ReadULong(), itemGuid = reader.ReadULong();
            ClaimModel claim = Claims.Get(claimId);
            WorldSession player = playerGuid == 0 ? session : PlayerManager.FindByGuid(playerGuid);
            if (claim == null || player == null)
            {
                Chat.System(session, claim == null ? $"No build site {claimId}." : $"Player {playerGuid} is not online.");
                return;
            }
            List<Entry> unplaced = Unplaced(claim);
            Entry loose = unplaced.FirstOrDefault(e => e.ItemGuid == itemGuid);
            if (loose != null)
            {
                unplaced.Remove(loose);
                SaveUnplaced(claim, unplaced);
                player.GiveItem(loose.Item, loose.Count);
                LootPinatas.Notify(player, loose.Item, loose.Count);
                log.Info($"{session.CharacterName} moved {loose.Count} x item {loose.Item} ({itemGuid}) from site {claimId} to {player.CharacterName}.");
            }
            else if (Placed(claim).FirstOrDefault(x => x.Entry.ItemGuid == itemGuid || x.Entry.PropGuid == itemGuid) is { Prop: not null } placed)
            {
                List<(uint Item, uint Count)> got = Props.TakeAsClaimItem(placed.Prop, player);
                log.Info($"{session.CharacterName} moved prop 0x{placed.Prop.Spawn.Guid:X} ({itemGuid}) from site {claimId} to "
                    + $"{player.CharacterName} as {string.Join(", ", got.Select(g => $"{g.Count} x {g.Item}"))}.");
            }
            else
            {
                Chat.System(session, $"Build site {claimId} holds no item {itemGuid}.");
                return;
            }
            SendDetails(session, claim);
        }

        /// <summary>
        /// SendInventoryDetails (ClaimsBase 0x0D; reader FUN_142da0d70, list FUN_142da3c70, element FUN_142d9fe40; case 0xC
        /// of the Claims dispatcher FUN_142dbecb0, which prints "Claims Inventory for claim: %llu" and "\tItemGuid: %llu"
        /// per item to the client's console) [P layout]: u8 0x0D, u64 claim guid (+0x18), u64 (+0x20), u32 n, n ×
        /// ClaimItemDetails {u64 item guid, u32, u32, u32, u32, u32, bool, u32, u64, u32, u32}. Only the guid is read by
        /// anything; the rest is filled [I] as item definition, tint, count, slot (the list index), 0, placed, 0, prop
        /// guid, 0, 0 — the second header u64 is 0 [I].
        /// </summary>
        public static void SendDetails(WorldSession session, ClaimModel claim)
        {
            List<Entry> items = Of(claim);
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(SendInventoryDetailsSub);
            w.Write(claim.Id);
            w.Write(0ul);
            w.Write((uint)items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                Entry e = items[i];
                w.Write(e.ItemGuid);
                w.Write(e.Item);
                w.Write(e.Tint);
                w.Write(e.Count);
                w.Write((uint)i);
                w.Write(0u);
                w.Write(e.PropGuid != 0);
                w.Write(0u);
                w.Write(e.PropGuid);
                w.Write(0u);
                w.Write(0u);
            }
            session.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, output.ToArray());
            // The console the client prints it to is not on screen by default: the same list as system lines.
            Chat.System(session, $"Build site {claim.Id}: {items.Count} item(s)"
                + string.Concat(items.Take(20).Select(e => $"; {e.ItemGuid} = {e.Count} x {e.Item}{(e.PropGuid != 0 ? $" (placed 0x{e.PropGuid:X})" : "")}")));
        }

        /// <summary>
        /// The site lapsed (SiteContents.Pack, its owner away): its unplaced items join the resources mailed to the owner,
        /// and the list is cleared in memory (the site's row is deleted with it). Before 2026-10-18 only a delete returned
        /// them (<see cref="ReturnUnplaced"/>); a lapse dropped them with the row (bughunt-build B10).
        /// </summary>
        public static List<(uint Item, uint Count)> TakeUnplaced(ClaimModel claim)
        {
            List<(uint Item, uint Count)> items = Unplaced(claim).Select(e => (e.Item, e.Count)).ToList();
            claim.Inventory = "";
            return items;
        }

        /// <summary>
        /// The site is deleted (Claims.Delete, its owner online): its unplaced items go to the owner's bag, the rest to
        /// the overflow tab (P0862: given back through the Overflow Inventory; <see cref="Inventory.Inventory.Add"/>
        /// overflows by
        /// itself). Its placed props are returned by the delete itself (as materials, P4870). The list is cleared in memory
        /// only: the site's row is deleted with it. Returns how many stacks.
        /// </summary>
        public static int ReturnUnplaced(WorldSession owner, ClaimModel claim)
        {
            List<Entry> unplaced = Unplaced(claim);
            foreach (Entry e in unplaced)
                owner.GiveItem(e.Item, e.Count);
            claim.Inventory = "";
            return unplaced.Count;
        }
    }
}
