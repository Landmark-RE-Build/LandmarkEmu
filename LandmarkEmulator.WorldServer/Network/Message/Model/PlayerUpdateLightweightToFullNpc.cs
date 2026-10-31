using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// PlayerUpdatePacketLightweightToFullNpc (0x26 sub 0x5C; 0.589 reader FUN_142b8f230, 0.695 FUN_142c4c3d0 "equal after
    /// the header", coverage-695.md): the answer to FullCharacterDataRequest 0x26/0x5E for a creature the client was shown
    /// as a lightweight NPC. The handler (FUN_142c45940) finds the entity by transient id, takes its attachments when the
    /// list isn't empty, and applies the rest (FUN_142cb9f50 — most fields only when non-zero; +0x250 / +0x258 only when
    /// they differ from "unset"), then clears the entity's lightweight flag (+0xcb4).
    /// <para>
    /// Layout (offsets in the client's packet object): 2-bit-length uint transient id (+0x18); u32 +0x1c, +0x20, +0x24;
    /// list CharacterAttachmentData (+0x28); string +0x48, +0x60, +0x78; u32 +0x90, +0x94, +0x98; 3 × u32 (+0x9c);
    /// 3 × u32 (+0xa8); u32 +0xb4, +0xb8, +0xbc (a text id: name), +0xc0; {u32, string, string, u32, string} (+0xe8,
    /// FUN_142b84f30); 4 × u32 (+0xd0); u32 +0xe0, +0xe4; u64 +0x140; u32 +0x148; Target (+0x150: u8 kind, 0 = none,
    /// FUN_142b69f30); list (+0x168, FUN_142ba3490); u32 +0x208, +0x20c; 4 × u32 (+0x210); 9 × u32 (+0x220..+0x240);
    /// u8 +0x244, u8 +0x248; u32 +0x24c, +0x250, +0x254; list (+0x2c8, FUN_142ba3250); u64 +0x258; u32 +0x260; list
    /// (+0x298, FUN_142ba5eb0); byte array (+0x2e0, FUN_142cb1fb0); u32 +0x264, +0x268, +0x26c, +0x270; list (+0x278,
    /// FUN_142b9b0b0). Nothing follows (the dispatcher checks the packet was read exactly).
    /// </para>
    /// <para>
    /// Every field carries the client's own default (its constructor FUN_142bcdf40): 0, except +0xb8 = 100, +0x248 = 2,
    /// +0x250 / +0x254 = 0xFFFFFFFF (DAT_14511a190 (0.589; 0.695 DAT_1451f31c8), "unset"), +0x26c / +0x270 = -1.0. So
    /// the creature keeps the model,
    /// name and position its lightweight spawn gave it. What live put in the other fields for creatures is not known
    /// (no capture; L85).
    /// </para>
    /// <para>
    /// +0x90 is the creature's disposition (<see cref="NpcDisposition"/>; trace/0695/full-npc-695.md): the 0.695 apply
    /// FUN_142d532f0 copies it unconditionally into entity +0x147c, which the client's hostility test FUN_142ea9630 and the
    /// nameplate HighlightMode (FUN_143201830) read. The entity constructor (FUN_142e88840) starts every character at 2
    /// (friendly), so sending 0 here is what turns a creature red; leaving it 0 for every creature is what made allies and
    /// neutrals show enemy nameplates (OPEN-LEADS L98). The handler drops a second copy once the entity is full (+0xcb4 is
    /// cleared), so a later change of side goes through <see cref="PlayerUpdateSetDisposition"/> instead.
    /// </para>
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdateLightweightToFullNpcWire)]
    public class PlayerUpdateLightweightToFullNpc : IWritable
    {
        public uint TransientId { get; set; }

        /// <summary>
        /// +0x90 → entity +0x147c: 0 hostile (red, the default for wild creatures and Enemy story creatures), 1 neutral,
        /// 2 friendly (ally). See <see cref="NpcDisposition"/>.
        /// </summary>
        public NpcDisposition Disposition { get; set; } = NpcDisposition.Hostile;

        public void Write(GamePacketWriter writer)
        {
            writer.WriteUIntWith2BitLength((int)TransientId);
            // +0x1c / +0x20 / +0x24: chat-bubble colours. The 0.695 apply FUN_142d532f0 copies them unconditionally over the
            // ProxiedCharacter defaults (FUN_142e88840: 0xFFFFFF, 0xD4E2F0, 1), so those defaults are sent, not 0
            // (trace/0695/agent-leads2-a.md L85) [P].
            writer.Write(0xFFFFFFu);
            writer.Write(0xD4E2F0u);
            writer.Write(1u);
            writer.Write(0u);                   // attachments (none: the client keeps the spawn's look)
            writer.Write("");
            writer.Write("");
            writer.Write("");
            writer.Write((uint)Disposition);    // +0x90 disposition → entity +0x147c
            U32(writer, 2);                     // +0x94, +0x98
            U32(writer, 3);                     // +0x9c
            U32(writer, 3);                     // +0xa8
            writer.Write(System.BitConverter.SingleToUInt32Bits(8f));   // +0xb4 → +0x740 "Expected speed": the default 8.0 (L85)
            writer.Write(100u);                 // +0xb8 (default 100)
            writer.Write(0u);                   // +0xbc name text id (0 = keep)
            writer.Write(0u);                   // +0xc0
            writer.Write(0u);                   // FUN_142b84f30: u32, string, string, u32, string
            writer.Write("");
            writer.Write("");
            writer.Write(0u);
            writer.Write("");
            U32(writer, 4);                     // +0xd0
            writer.Write(0u);                   // +0xe0
            writer.Write(0u);                   // +0xe4
            writer.Write(0ul);                  // +0x140
            writer.Write(0u);                   // +0x148
            writer.Write((byte)0);              // Target: none
            writer.Write(0u);                   // list +0x168
            writer.Write(0u);                   // +0x208
            writer.Write(0u);                   // +0x20c
            U32(writer, 4);                     // +0x210
            U32(writer, 9);                     // +0x220 .. +0x240
            writer.Write((byte)0);              // +0x244
            writer.Write((byte)2);              // +0x248 (default 2)
            writer.Write(0u);                   // +0x24c
            writer.Write(uint.MaxValue);        // +0x250 unset
            writer.Write(uint.MaxValue);        // +0x254 unset
            writer.Write(0u);                   // list +0x2c8
            writer.Write(0ul);                  // +0x258 (no guid)
            writer.Write(0u);                   // +0x260
            writer.Write(0u);                   // list +0x298
            writer.Write(0u);                   // byte array +0x2e0
            writer.Write(0u);                   // +0x264
            writer.Write(0u);                   // +0x268
            writer.Write(-1f);                  // +0x26c
            writer.Write(-1f);                  // +0x270
            writer.Write(0u);                   // list +0x278
        }

        private static void U32(GamePacketWriter writer, int count)
        {
            for (int i = 0; i < count; i++)
                writer.Write(0u);
        }
    }
}
