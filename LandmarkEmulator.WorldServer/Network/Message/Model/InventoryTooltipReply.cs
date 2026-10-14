using LandmarkEmulator.Shared.Network;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Inventory 0x12 ItemTooltipReply (reader FUN_142e784e0, shown by FUN_143463610). The tooltip is built from an
    /// item record whose blob is the item definition exactly as in the item definitions packet; the client parses it.
    /// A definition tooltip (hovering a collection entry) is keyed by definition id, an instance tooltip by guid.
    /// Written as raw bytes behind the InventoryBase opcode, like the other inventory sub-opcodes.
    /// </summary>
    public static class InventoryTooltipReply
    {
        public const ushort SubOpcode = 0x12;

        public static byte[] Build(bool definitionTooltip, bool full,
            Game.Inventory.ItemDefinitionManager.ItemDefinition definition, ulong itemGuid)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write((byte)SubOpcode);
            writer.Write((byte)0);
            writer.Write(definitionTooltip);
            writer.Write(full);

            // Item record (FUN_142e78e10).
            // Display data: name and description text ids and icon, then the definition id the client keys
            // definition tooltips by.
            writer.Write(definition.NameId);
            writer.Write(definition.DescriptionId);
            writer.Write(definition.ImageSetId);
            writer.Write(0u);
            writer.Write(definition.Id);
            writer.Write((uint)definition.Categories.Count); // categories (tooltip +0x180)
            foreach (uint category in definition.Categories)
                writer.Write(category);
            writer.Write(0u);                                 // second int list (tooltip +0x198)
            // Third int list (tooltip +0x1b0): the categories the tooltip names on its "Categories" line and picks its
            // display category from (FUN_1436a5ac0), which decides which stat lines show (FUN_1436a3a80).
            writer.Write((uint)definition.Categories.Count);
            foreach (uint category in definition.Categories)
                writer.Write(category);
            writer.Write(0u);
            // Blob: a "has definition" byte, then the compressed definition (FUN_143463610 skips the first byte).
            writer.Write((uint)definition.RawData.Length + 1);
            writer.Write((byte)1);
            writer.WriteBytes(definition.RawData);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(itemGuid);          // [0x40]: instance tooltips are keyed by guid
            writer.Write(0u);
            writer.Write(0u);
            // Stats (record +0xc0, FUN_142e78ba0): {u32 stat, u32 name text, f32 value, f32, f32 base value}. The
            // stat line (FUN_143466d50) prints the name text and the value (formatted by the stat's
            // CalculatedStatDefinitions.txt row), in another colour when value != base. Gathering tools show
            // harvesting area, speed and damage.
            var stats = Game.Abilities.ToolDisplayStats(definition.Id) is var (area, speed, damage)
                ? new[] { (3u, area), (13u, speed), (12u, damage) } : System.Array.Empty<(uint, float)>();
            writer.Write((uint)stats.Length);
            foreach ((uint stat, float value) in stats)
            {
                writer.Write(stat);
                writer.Write(Game.StatNames.NameText(stat));
                writer.Write(value);
                writer.Write(value);
                writer.Write(value);
            }
            writer.Write(0ul);
            writer.Write(0u);
            writer.Write(0ul);
            writer.Write((byte)0);           // flags (0x80 = obsolete item)
            writer.Write("");
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write((byte)0);
            writer.Write(false);             // optional block absent
            writer.Write("");
            writer.Write("");
            writer.Write(0u);

            writer.Write(0u);                // related items (none)
            // Tooltip flags (reply +0x1c8, the last field; FUN_1436a9020 hands it to FUN_1434655a0, which writes one line
            // per bit: 0x40 NoSale, 0x20 NoTrade, 0x10 RemovedOnUse, 8 NoDelete, 4 NoUpgrade, 2 UniqueEquip, 1 NoSalvage).
            // The live server filled it; the only one the definition settles for sure is NoTrade (first flag byte bit 7,
            // the same bit the client's Lua NoTrade reads). Other definition bits: byte 0 bit 3 on 2,180 stackable items,
            // bit 6 on founder tools and trophies; byte 1 bit 1 on 97 gathering tools, bit 3 on Lumens and builder's bags,
            // bit 4 on 690 items the client doesn't warn about in overflow (FUN_142e83a30); byte 2 bit 7 on 21 accessories.
            writer.Write(definition.NoTrade ? 0x20u : 0u);
            return output.ToArray();
        }
    }
}
