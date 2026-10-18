using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System;
using System.Text;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Zone family 0xDD (the client's BaseClientPcDataPacket): u16 base 0xDD, u8 sub, then the sub's body (header writer
    /// FUN_142fc3750: u16 from packet +8, u8 from +0x10) [P]. The 0.695 client builds three subs (every <c>= 0xdd;</c>
    /// packet constructor in re/decomp_all_695):
    /// <list type="bullet">
    /// <item>1 SetSpeechPackPacket {u32 speech pack} (Lua RequestSpeechPack; send helper FUN_142fc59f0) — not handled yet;</item>
    /// <item>2 GetSpeechPackListPacket {} (Lua RequestSpeechPackList; FUN_142fc56b0) — not handled yet;</item>
    /// <item>4 PlayerFilterUpdatePacket (<see cref="PlayerFilterUpdate"/>): the bag filters (Game.InventoryFilters).</item>
    /// </list>
    /// The S→C side of the family (dispatcher FUN_142fcf8b0) reads only subs 1 (u32 into player +0x280) and 3
    /// (SpeechPackListPacket); a sub 4 from the server would be ignored, so filter updates get no reply [P].
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ClientPcDataBase)]
    public class ClientPcDataRequest : IReadable
    {
        public const byte SetSpeechPack = 1, GetSpeechPackList = 2, PlayerFilterUpdate = 4;

        /// <summary>The u8 sub after the u16 base; 0 when the body is empty.</summary>
        public byte SubOpcode { get; private set; }
        /// <summary>The sub's body (after the sub byte).</summary>
        public byte[] Body { get; private set; } = Array.Empty<byte>();

        public void Read(GamePacketReader reader)
        {
            if (reader.BytesRemaining == 0)
                return;
            SubOpcode = reader.ReadByte();
            Body = reader.ReadBytes(reader.BytesRemaining);
        }
    }

    /// <summary>
    /// PlayerFilterUpdatePacket (zone 0xDD sub 4, C→S, Zone channel 0) [P]: the client reports one change to its bag
    /// filters (InventoryFilters::ClientInventoryFilterManager, player +0x7ae60). Body after the u8 sub, as the writer
    /// FUN_142fc3d20 writes it: u32 FilterId (+0x18), string FilterName (+0x20), string FilterString (+0x38), u32 Op
    /// (+0x50); strings are u32 length + characters, no terminator (FUN_142d4b510). Constructor FUN_142fc8110 (base 0xDD,
    /// sub 4), sender FUN_142fd2d40 (zone send helper FUN_142fc5850, channel 0), called by the manager's
    /// <list type="bullet">
    /// <item>AddFilter FUN_142efd5e0 (vtable slot 1; Lua ClientInventory.AddFilter(id, name, filterString), native
    /// FUN_1435df160): Op 0 with the filter's id (id 0 from Lua means "new": it gets the manager's NextFilterId, which is
    /// then counted up; an id at or above NextFilterId moves NextFilterId past it), the name and the filter string. Sent
    /// only when both strings are non-empty; an existing id is updated in place. With an empty string and an existing id
    /// the filter is removed instead (Op 1); otherwise nothing is sent.</item>
    /// <item>RemoveFilter FUN_142efd330 (slot 2; Lua ClientInventory.RemoveFilter(id), FUN_1435df100): Op 1 with the id and
    /// the removed filter's name and string; only for an id it has (id 0 never).</item>
    /// <item>ResetFilters FUN_142efd4f0 (slot 3; Lua ClientInventory.ResetFilters(), FUN_1435df270): every filter removed,
    /// Op 2 with id 0 and two copies of the same constant string (DAT_144c40a60; [I] the empty string — the server
    /// ignores both). NextFilterId is not reset.</item>
    /// </list>
    /// Every sender checks that the player exists (DAT_145431a20 != 0).
    /// </summary>
    public class PlayerFilterUpdate : IWritable
    {
        /// <summary>The Op values (FUN_142fd2d40's second argument at its three callers) [P].</summary>
        public const uint AddOrUpdate = 0, Remove = 1, Reset = 2;

        public uint FilterId { get; set; }
        public string FilterName { get; set; } = "";
        public string FilterString { get; set; } = "";
        public uint Op { get; set; }
        /// <summary>The byte lengths of the two strings as sent (UTF-8 here; checked against the server's caps).</summary>
        public int FilterNameBytes { get; private set; }
        public int FilterStringBytes { get; private set; }

        /// <summary>
        /// Reads the body (after the sub byte). False when it is short, a length runs past the end, or bytes are left over
        /// (the client writes exactly these four fields). Never throws: the lengths come from the client.
        /// </summary>
        public static bool TryParse(byte[] body, out PlayerFilterUpdate update)
        {
            update = null;
            if (body == null)
                return false;
            int at = 0;
            if (!TryU32(body, ref at, out uint id) || !TryString(body, ref at, out string name, out int nameBytes)
                || !TryString(body, ref at, out string filter, out int filterBytes) || !TryU32(body, ref at, out uint op)
                || at != body.Length)
                return false;
            update = new PlayerFilterUpdate
            {
                FilterId = id, FilterName = name, FilterString = filter, Op = op,
                FilterNameBytes = nameBytes, FilterStringBytes = filterBytes
            };
            return true;
        }

        private static bool TryU32(byte[] body, ref int at, out uint value)
        {
            value = 0;
            if (body.Length - at < 4)
                return false;
            value = BitConverter.ToUInt32(body, at);
            at += 4;
            return true;
        }

        private static bool TryString(byte[] body, ref int at, out string value, out int length)
        {
            value = "";
            length = 0;
            if (!TryU32(body, ref at, out uint size) || size > (uint)(body.Length - at))
                return false;
            length = (int)size;
            value = length == 0 ? "" : Encoding.UTF8.GetString(body, at, length);
            at += length;
            return true;
        }

        /// <summary>The body as the client writes it (for tests and tools; the server never sends this packet).</summary>
        public void Write(GamePacketWriter writer)
        {
            writer.Write(FilterId);
            writer.Write(FilterName);
            writer.Write(FilterString);
            writer.Write(Op);
        }
    }
}
