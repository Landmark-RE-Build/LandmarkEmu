using System;
using System.Collections.Generic;

namespace LandmarkEmulator.Database.Character.Model
{
    public class CharacterModel
    {
        public ulong Id { get; set; }
        public ulong AccountId { get; set; }
        public string Name { get; set; }
        public byte Gender { get; set; }
        public byte Race { get; set; }
        public uint SkinTint { get; set; }
        public uint ProfileTypeId { get; set; }
        public ulong LastServerId { get; set; }
        public DateTime? LastOnline { get; set; }
        public DateTime CreateTime { get; set; }
        public DateTime? DeleteTime { get; set; }
        public string OriginalName { get; set; }

        /// <summary>
        /// Last known world position. All zero means the character has never been in the world (use the default spawn).
        /// </summary>
        /// <summary>The landscape (StaticZoneData zone id, e.g. 406 Sleeping Ridge) the position is on.</summary>
        public uint ZoneId { get; set; } = 406;
        /// <summary>Times the character entered the world (SendSelfToClient LoginCount, shown by /played).</summary>
        public uint LoginCount { get; set; }
        /// <summary>Minutes spent in the world (SendSelfToClient PlayedMinutes, shown by /played).</summary>
        public uint PlayedMinutes { get; set; }
        /// <summary>The player's profile text (0.695 Bio, at most 512 characters: client "BioLengthMax").</summary>
        public string Bio { get; set; } = "";
        /// <summary>
        /// Chat bubble colours and size (CommandPacketSetChatBubbleColor 0x1E/0x15 {u32 fg, u32 bg, u32 size, u64 guid},
        /// Lua Ui.SetChatColorFgBgSz): the character's own, shown to others through LightweightToFullPc and 0x15. Defaults
        /// are the client's for a new character entity (FUN_142e88840: FUN_143b94310 0xFFFFFF, FUN_143b941a0 0xD4E2F0, 1).
        /// </summary>
        public uint ChatBubbleForeground { get; set; } = 0xFFFFFF;
        public uint ChatBubbleBackground { get; set; } = 0xD4E2F0;
        public uint ChatBubbleSize { get; set; } = 1;
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float PositionZ { get; set; }

        /// <summary>
        /// Last known facing as a quaternion. All zero means none was saved.
        /// </summary>
        public float RotationX { get; set; }
        public float RotationY { get; set; }
        public float RotationZ { get; set; }
        public float RotationW { get; set; }

        public ICollection<CharacterCustomisationModel> Customisation { get; set; } = new HashSet<CharacterCustomisationModel>();
        public ICollection<CharacterItemModel> Items { get; set; } = new HashSet<CharacterItemModel>();
    }
}
