using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model.Shared;
using System;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// The item tooltip requests (InventoryBase 0x2D), read the way each of their senders fills them
    /// (docs/request-variants.md):
    /// <list type="bullet">
    /// <item>0x0F ItemInstanceTooltipRequest (builder FUN_143519570, writer FUN_14350b970): u32 owner type, u64 owner guid,
    /// u64 item guid, u32, string data source, u32 mode [P]. Senders: ClientInventory.RequestDetailsForItem /
    /// ...Windowed (owner type 1, owner = the second Lua argument or the player: the bag, and the Inspect window's
    /// gear with the inspected player's guid), RequestDetailsForItemInChest (owner type 3, owner = the chest:
    /// ToolTipWidget / TooltipItem "item|chest"), and the upgrade station (FUN_142f39870 / FUN_142f3d970: type 1, mode 2,
    /// data source "Crafting.Upgrade.Tooltip").</item>
    /// <item>0x10 ItemInstanceFullTooltipRequest (FUN_14351ae30 from RequestDetailsForItemInMail; writer FUN_14350c640):
    /// the mail attachment's whole item record (FUN_14350baa0, the layout of <see cref="InventoryItem"/>: definition
    /// block, then u64 guid, ...) [P].</item>
    /// <item>0x11 ItemDefinitionTooltipRequest: u32 definition, u8 full (RequestDetailsForItemByDefId, the mail coin
    /// pane's 0x392 = 914).</item>
    /// </list>
    /// Before 2026-10-21 the server read 0x0F and 0x10 as an inventory move location {u32 type, u32 container, u32 slot,
    /// [u64 guid for types 2/3/5]}, which put the item guid in the right place only for type 1 and never saw the owner:
    /// chest items, the inspected player's gear and mail attachments got no tooltip.
    /// </summary>
    public readonly record struct TooltipRequest(uint OwnerType, ulong OwnerGuid, ulong ItemGuid, uint DefinitionId, uint Mode)
    {
        /// <summary>Owner types: 1 a player's own items (bag / equipment), 3 a chest.</summary>
        public const uint PlayerOwner = 1, ChestOwner = 3;

        /// <summary>Reads 0x0F's body (after the u16 sub). False when it runs short.</summary>
        public static bool TryReadInstance(byte[] body, out TooltipRequest request)
        {
            request = default;
            if (body == null || body.Length < 20)
                return false;
            try
            {
                var r = new GamePacketReader(body);
                uint ownerType = r.ReadUInt();
                ulong owner = r.ReadULong();
                ulong item = r.ReadULong();
                uint mode = 0;
                if (r.BytesRemaining >= 4)
                {
                    r.ReadUInt();
                    if (r.BytesRemaining >= 4)
                    {
                        r.ReadString();
                        if (r.BytesRemaining >= 4)
                            mode = r.ReadUInt();
                    }
                }
                request = new TooltipRequest(ownerType, owner, item, 0, mode);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Reads 0x10's body: the item record's definition and guid. False when it runs short.</summary>
        public static bool TryReadFull(byte[] body, out TooltipRequest request)
        {
            request = default;
            if (body == null || body.Length < 4)
                return false;
            try
            {
                var r = new GamePacketReader(body);
                var definition = new InventoryItem.DefinitionData();
                definition.Read(r);
                ulong guid = r.BytesRemaining >= 8 ? r.ReadULong() : 0;
                request = new TooltipRequest(0, 0, guid, definition.DefinitionId, 0);
                return definition.DefinitionId != 0;
            }
            catch (Exception)
            {
                // A record cut short after its definition id still names the item kind.
                uint id = BitConverter.ToUInt32(body, 0);
                request = new TooltipRequest(0, 0, 0, id, 0);
                return id != 0;
            }
        }

        /// <summary>Writes a 0x0F body the way the client does (tests, tools).</summary>
        public static byte[] WriteInstance(uint ownerType, ulong owner, ulong item, string dataSource = "", uint mode = 0)
        {
            var bytes = new System.Collections.Generic.List<byte>();
            var w = new GamePacketWriter(bytes);
            w.Write(ownerType);
            w.Write(owner);
            w.Write(item);
            w.Write(0u);
            w.Write(dataSource ?? "");
            w.Write(mode);
            return bytes.ToArray();
        }
    }
}
