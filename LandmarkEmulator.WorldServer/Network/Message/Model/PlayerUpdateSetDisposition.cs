using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// A character's disposition towards players: the client entity's +0x147c (trace/0695/full-npc-695.md, OPEN-LEADS L98).
    /// <list type="bullet">
    /// <item>0 Hostile: FUN_142ea9630 (is hostile) is true for an NPC whose FactionId (+0x1448) is outside 1-3 exactly when
    /// this is 0; the nameplate gets HighlightMode 1 (FUN_143201830); a click on it with no target selects it
    /// (FUN_142d11210 → SetCurrentTarget).</item>
    /// <item>1 Neutral: FUN_142eaa260 (is neutral) — never hostile to anyone; nameplate HighlightMode 0 (no highlight).</item>
    /// <item>2 Friendly: FUN_142ea8f90 (is ally of the player) is true; hostile only to NPCs with a different, non-neutral
    /// disposition (an ally against an enemy creature); nameplate HighlightMode 2. The ProxiedCharacter constructor
    /// (FUN_142e88840) starts every entity at 2.</item>
    /// </list>
    /// The values match the story spawner's Reaction (Creatures: 0 enemy, 1 neutral, 2 ally).
    /// </summary>
    public enum NpcDisposition : uint
    {
        Hostile = 0,
        Neutral = 1,
        Friendly = 2
    }

    /// <summary>
    /// PlayerUpdatePacketSetDisposition (PlayerUpdate 0x26 sub 0x14; 0.695 wire: u16 0x26 + u32 sub, written by the session's
    /// 0.695 wire layer): changes the disposition of a character the client already has, lightweight or full.
    /// <para>
    /// [P] 0.695: id table cPlayerUpdatePacketIdSetDisposition = 0x14 (re/out/packet_ids.tsv, coverage-695.md row 0x14);
    /// RTTI PlayerUpdatePacketSetDisposition (re/out/rtti_695.tsv, vftable 1446a9830); dispatcher FUN_142cdfa60 case 0x13
    /// (the switch runs on sub − 1); reader FUN_142c4f960 → FUN_142c2b5a0: u16 base +0x8, u32 sub +0x10, u64 guid +0x18,
    /// u32 disposition +0x20 — nothing else. 0.589 reader FUN_142bb6160 → FUN_142b91b30.
    /// </para>
    /// <para>
    /// [P] Handler (case 0x13): finds the entity by guid (FUN_142f85390), stores the value with FUN_142d50410 (the only
    /// setter of +0x147c besides the full-data apply FUN_142d532f0 and the two spawn paths), then, when that entity is the
    /// player's current target and the new value is non-zero, clears the target (FUN_142d4e890 with an empty guid).
    /// </para>
    /// <para>
    /// Needed because <see cref="PlayerUpdateLightweightToFullNpc"/> is applied only once per entity (FUN_142cdf400 checks
    /// the lightweight flag +0xcb4 and clears it), so it can't carry a later change of side. 18 bytes on the wire (0.695).
    /// </para>
    /// </summary>
    /// <remarks>
    /// The opcode is the PlayerUpdate wire form (base 0x26, sub 0x14) like the other <c>...Wire</c> values
    /// (<see cref="ZoneMessageOpcode.PlayerUpdateSetDispositionWire"/>).
    /// </remarks>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdateSetDispositionWire)]
    public class PlayerUpdateSetDisposition : IWritable
    {
        /// <summary>PlayerUpdate base 0x26, sub 0x14 (the session widens the sub to a u32 for 0.695).</summary>
        public const uint WireOpcode = 0x260014;

        /// <summary>The character's guid (packet +0x18).</summary>
        public ulong Guid { get; set; }

        /// <summary>The new disposition (packet +0x20 → entity +0x147c).</summary>
        public NpcDisposition Disposition { get; set; }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(Guid);
            writer.Write((uint)Disposition);
        }
    }
}
