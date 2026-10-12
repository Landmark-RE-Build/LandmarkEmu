using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// A client packet that is a u8 sub-opcode then a body the game code reads per sub (most "…Base" packets). The
    /// subclasses only carry the [ZoneMessage] opcode.
    /// </summary>
    public abstract class SubRequest8 : IReadable
    {
        public byte SubOpcode { get; private set; }
        public byte[] Body { get; private set; }

        public void Read(GamePacketReader reader)
        {
            SubOpcode = reader.ReadByte();
            Body = reader.ReadBytes(reader.BytesRemaining);
        }
    }

    /// <summary>As <see cref="SubRequest8"/> with a u16 sub-opcode.</summary>
    public abstract class SubRequest16 : IReadable
    {
        public ushort SubOpcode { get; private set; }
        public byte[] Body { get; private set; }

        public void Read(GamePacketReader reader)
        {
            SubOpcode = reader.ReadUShort();
            Body = reader.ReadBytes(reader.BytesRemaining);
        }
    }

    /// <summary>As <see cref="SubRequest8"/> with a u32 sub-opcode.</summary>
    public abstract class SubRequest32 : IReadable
    {
        public uint SubOpcode { get; private set; }
        public byte[] Body { get; private set; }

        public void Read(GamePacketReader reader)
        {
            SubOpcode = reader.ReadUInt();
            Body = reader.ReadBytes(reader.BytesRemaining);
        }
    }

    /// <summary>
    /// Marks entities as having interactions (PlayerUpdate 0x6B, reader FUN_142bb2440, handler FUN_142ee6aa0 sets
    /// entity +0x1461 bit 0x40). The client then asks for their details with <see cref="InteractionInfoRequest"/>.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdateDynamicInteractionFlaggingWire)]
    public class DynamicInteractionFlagging : IWritable
    {
        public bool Interactable { get; set; } = true;
        public List<ulong> Guids { get; } = new();

        public void Write(GamePacketWriter writer)
        {
            writer.Write(Interactable);
            writer.Write((uint)Guids.Count);
            foreach (ulong guid in Guids)
                writer.Write(guid);
        }
    }

    /// <summary>
    /// Client asks how it can interact with flagged entities (PlayerUpdate 0x69; sent again every few seconds while
    /// they are near).
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdateInteractionInfoRequestWire)]
    public class InteractionInfoRequest : IReadable
    {
        public List<ulong> Guids { get; } = new();

        public void Read(GamePacketReader reader)
        {
            uint count = reader.ReadUInt();
            for (uint i = 0; i < count && reader.BytesRemaining >= 8; i++)
                Guids.Add(reader.ReadULong());
        }
    }

    /// <summary>
    /// PlayerUpdate 0x6A (reader FUN_142bb27b0 / entry FUN_142b86ae0, handler FUN_142ee6b90).
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdateInteractionInfoReplyWire)]
    public class InteractionInfoReply : IWritable
    {
        /// <summary>
        /// Interaction type (entity +0x4a28): 0 none, 1 not known yet (client asks again), 3 interactable (hover
        /// cursor, highlight, click sends Command InteractRequest).
        /// </summary>
        public const uint None = 0, Interactable = 3;

        public class Entry
        {
            public ulong Guid { get; set; }
            public uint Type { get; set; }
            /// <summary>
            /// Mouse cursors (Cursors.txt ids) the client shows while hovering the entity (FUN_142e26a10 → entity
            /// +0x2188 in reach / +0x218c out of reach for an interactable; +0x2190 / +0x2194 otherwise; read by the
            /// hover handler FUN_143328810). 0 = the normal arrow, 10 = cursor_interaction_generic.
            /// </summary>
            public uint CursorInReach { get; set; }
            public uint CursorOutOfReach { get; set; }
            /// <summary>Negative keeps the client's value.</summary>
            public float Unknown20 { get; set; } = -1f;
            /// <summary>Interaction range in metres; negative keeps the client's value.</summary>
            public float Range { get; set; } = -1f;
            /// <summary>
            /// Hover outline (entity +0x4a30): the interaction highlighter (FUN_143324bd0) draws no outline while it is 0.
            /// </summary>
            public bool Outline { get; set; }
        }

        public List<Entry> Entries { get; } = new();

        public void Write(GamePacketWriter writer)
        {
            writer.Write((uint)Entries.Count);
            foreach (Entry entry in Entries)
            {
                writer.Write(entry.Guid);
                writer.Write(entry.Type);
                writer.Write(entry.CursorInReach);
                writer.Write(entry.CursorOutOfReach);
                writer.Write(entry.CursorInReach);
                writer.Write(entry.CursorOutOfReach);
                writer.Write(0u);
                writer.Write(entry.Unknown20);
                writer.Write(entry.Range);
                writer.Write(entry.Outline);
            }
        }
    }

    /// <summary>
    /// A Command request from the client: CommandBase 0x1E, a u16 sub-opcode, then its body (handled per
    /// sub-opcode). Sub ids per FUN_14340fc20: 9 InteractRequest (u64 guid), 0x10 AddFriendRequest (string name, u64),
    /// 0x12 RemoveFriendRequest, 0x16 SelectPlayer, 0x4C InteractionItem
    /// (u64 item guid: an item was used, e.g. right-clicked in the inventory).
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.CommandBase)]
    public class CommandRequest : IReadable
    {
        public const ushort InteractRequest = 0x09, AddFriendRequest = 0x10, RemoveFriendRequest = 0x12, SelectPlayer = 0x16,
            InteractionItem = 0x4C, ClientCommand = 0x43, BuilderMode = 0x5A, FreeInteractionNpc = 0x17, IgnoreRequest = 0x20;

        public ushort SubOpcode { get; private set; }
        public byte[] Body { get; private set; }

        public void Read(GamePacketReader reader)
        {
            SubOpcode = reader.ReadUShort();
            Body = reader.ReadBytes(reader.BytesRemaining);
        }
    }

    /// <summary>
    /// A FriendBase packet from the client: u8 sub, then its body. Seen: sub 0x0B on world entry
    /// {u32 1, string, string, string} (friends window settings; nothing to answer).
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.FriendBase)]
    public class FriendRequest : SubRequest8 { }

    /// <summary>A GroupsBase packet from the client: u8 sub, then its body (see Game.Groups).</summary>
    [ZoneMessage(ZoneMessageOpcode.GroupsBase)]
    public class GroupRequest : SubRequest8 { }

    /// <summary>
    /// A Notifications packet from the client (base 1, bytes 01 00): u32 sub, then its body. Sub 3 is
    /// UpdateNotification {u64 notification id, u32 state} (state 4 = the Game Action Queue entry is finished).
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.Notifications)]
    public class NotificationRequest : SubRequest32 { }

    /// <summary>A ClaimVendorBase packet from the client (0x112, wire 12 01): u32 sub, then u64 vendor and the body (see Game.Vendors).</summary>
    [ZoneMessage(ZoneMessageOpcode.ClaimVendorBase)]
    public class ClaimVendorRequest : SubRequest32 { }

    /// <summary>An EncounterBase packet from the client (0x2C): i16 sub, then {u32 pending, u32 encounter, …} (see Game.Encounters).</summary>
    [ZoneMessage(ZoneMessageOpcode.EncounterBase)]
    public class EncounterRequest : SubRequest16 { }

    /// <summary>A GuildBase packet from the client: u16 sub, then its body (see Game.Guilds).</summary>
    [ZoneMessage(ZoneMessageOpcode.GuildBase)]
    public class GuildRequest : SubRequest16 { }

    /// <summary>A VoxelConstructionBase packet from the client: u8 sub, then its body (see Game.Building).</summary>
    [ZoneMessage(ZoneMessageOpcode.VoxelConstructionBase)]
    public class ConstructionRequest : SubRequest8 { }

    /// <summary>A LootPinataBase packet from the client: u8 sub, then its body (see Game.LootPinatas).</summary>
    [ZoneMessage(ZoneMessageOpcode.LootPinataBase)]
    public class LootPinataRequest : SubRequest8 { }

    /// <summary>A TemplateBase packet from the client (0xF0): u8 sub, then its body (see Game.Templates).</summary>
    [ZoneMessage(ZoneMessageOpcode.TemplateBase)]
    public class TemplateRequest : SubRequest8 { }

    /// <summary>A TradeBase packet from the client: u8 sub, then its body.</summary>
    [ZoneMessage(ZoneMessageOpcode.TradeBase)]
    public class TradeRequest : SubRequest8 { }

    /// <summary>A TargetBase packet from the client (0xB5): u16 sub, then its body.</summary>
    [ZoneMessage(ZoneMessageOpcode.TargetBase)]
    public class TargetRequest : SubRequest16 { }

    /// <summary>A ChestBase packet from the client: u32 sub, then its body.</summary>
    [ZoneMessage(ZoneMessageOpcode.ChestBase)]
    public class ChestRequest : SubRequest32 { }

    /// <summary>An InGamePurchaseBase packet from the client: u16 sub, then its body.</summary>
    [ZoneMessage(ZoneMessageOpcode.InGamePurchaseBase)]
    public class InGamePurchaseRequest : SubRequest16 { }

    /// <summary>A ToolBase packet from the client (0x117, wire 17 01): u8 sub (1 Activate: the client switched build tool).</summary>
    [ZoneMessage(ZoneMessageOpcode.ToolBase)]
    public class ToolRequest : SubRequest8 { }

    /// <summary>A PaletteBase packet from the client (0xF5): u16 sub, then its body (see Game.Palettes).</summary>
    [ZoneMessage(ZoneMessageOpcode.PaletteBase)]
    public class PaletteRequest : SubRequest16 { }

    /// <summary>An AchievementBase packet from the client (0x92): u8 sub, then its body (see Game.Achievements).</summary>
    [ZoneMessage(ZoneMessageOpcode.AchievementBase)]
    public class AchievementRequest : SubRequest8 { }

    /// <summary>A CharacterCustomization packet from the client (0x11C): u8 sub, then its body (see Game.Barbershop).</summary>
    [ZoneMessage(ZoneMessageOpcode.CharacterCustomizationBase)]
    public class CharacterCustomizationRequest : SubRequest8 { }

    /// <summary>A FirstTimeEventBase packet from the client (0x6D): u8 sub, then its body (see Game.FirstTimeEvents).</summary>
    [ZoneMessage(ZoneMessageOpcode.FirstTimeEventBase)]
    public class FirstTimeEventRequest : SubRequest8 { }

    /// <summary>A PlayerTitleBase packet from the client (0x94): u8 sub, then its body (see Game.Titles).</summary>
    [ZoneMessage(ZoneMessageOpcode.PlayerTitleBase)]
    public class PlayerTitleRequest : SubRequest8 { }

    /// <summary>BugSubmission (base 0x55, u16 sub; sub 1 AddBug: the bug report window, see Game.Reports).</summary>
    [ZoneMessage(ZoneMessageOpcode.BugSubmissionBase)]
    public class BugSubmissionRequest : SubRequest16 { }

    /// <summary>ClaimReportSubmission (base 0x56, u16 sub; sub 1 AddReport: report a build site, see Game.Reports).</summary>
    [ZoneMessage(ZoneMessageOpcode.ClaimReportSubmissionBase)]
    public class SiteReportRequest : SubRequest16 { }

    /// <summary>An InstancedGameBase packet from the client (base 0x10D, u32 sub; see Game.InstancedGames).</summary>
    [ZoneMessage(ZoneMessageOpcode.InstancedGameBase)]
    public class InstancedGameRequest : SubRequest32 { }

    /// <summary>UpdatePropInteractionState 0xFD from the client (no sub): its whole body (see Game.PropSettings).</summary>
    [ZoneMessage(ZoneMessageOpcode.UpdatePropInteractionState)]
    public class PropInteractionStateRequest : IReadable
    {
        public byte[] Body { get; private set; }

        public void Read(GamePacketReader reader) => Body = reader.ReadBytes(reader.BytesRemaining);
    }

    /// <summary>A GameScriptingBase packet from the client (base 0x111, u8 sub; ObjectScripting, see Game.StoryTools).</summary>
    [ZoneMessage(ZoneMessageOpcode.GameScriptingBase)]
    public class GameScriptingRequest : SubRequest8 { }
}
