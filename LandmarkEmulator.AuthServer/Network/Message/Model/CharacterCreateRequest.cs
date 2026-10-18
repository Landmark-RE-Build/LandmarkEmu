using LandmarkEmulator.Shared.Game.Entity.Static;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System.Collections.Generic;

namespace LandmarkEmulator.AuthServer.Network.Message.Model
{
    /// <summary>
    /// CharacterCreateRequest (login 0x05), 0.695 client [P]. Lua CharacterSelect.CreateCharacter → FUN_1430bb570 fills
    /// the request from the login state (DAT_1454311c8 +0x32490: +0x40 empire, +0x48 profile type, +0x50 gender, +0x58
    /// name; the create state at +0x20: customization at +0xf0, gear set +0xf78), FUN_1430b1aa0 serializes the payload
    /// (FUN_1430ad2f0) and FUN_143a5aac0 sends {u64 server id, u32 payload length, payload} (the payload is an
    /// Array&lt;u8&gt; written as u32 count + bytes, FUN_142da2e50, the same as CharacterLoginRequest's).
    /// Payload: u8 empire, u32 profile type, u32 gender, string name, CharacterCustomizationData (FUN_142f59020: slot
    /// list, u32 skin tint, string, u32 race, u32 gender), u32 gear set.
    /// </summary>
    [AuthMessage(AuthMessageOpcode.CharacterCreateRequest, ProtocolVersion.LoginUdp_10)]
    public class CharacterCreateRequest : IReadable
    {
        public ulong ServerId { get; set; }
        /// <summary>Byte count of everything after this field (the client's payload Array&lt;u8&gt;) [P].</summary>
        public uint PayloadLength { get; set; }
        public byte EmpireId { get; set; }
        public uint ProfileTypeId { get; set; }
        /// <summary>
        /// Login state +0x50: the gender picked in character create (Lua CharacterSelect.SetGender FUN_1435d9e30 →
        /// FUN_1430c3ee0 writes it, 0 becomes 1; GetGender FUN_1435d9e80 reads it) [P]. The customization block sends the
        /// same field again as <see cref="Gender"/>, which the server uses.
        /// </summary>
        public uint SelectedGender { get; set; }
        public string Name { get; set; }
        public List<(uint, uint, uint)> CustomisationOptions { get; set; } = new();
        public uint SkinTint { get; set; }
        /// <summary>
        /// The customization block's string (CharacterCustomizationData +0x20, Shared CharacterModelInfo.MaterialOverride)
        /// [P: FUN_142f59020 writes a string here with FUN_142d4b510]. Character create never sets it (FUN_1430bccf0 fills
        /// only the slots, skin tint, race and gender), so the client always sends "". It used to be read as a u32,
        /// which only worked because an empty string's length is 0.
        /// </summary>
        public string MaterialOverride { get; set; } = "";
        public Race Race { get; set; }
        public Gender Gender { get; set; }
        public uint StartingOutfit { get; set; }

        public void Read(GamePacketReader reader)
        {
            ServerId = reader.ReadULong();
            PayloadLength = reader.ReadUInt();
            EmpireId = reader.ReadByte();
            ProfileTypeId = reader.ReadUInt();
            SelectedGender = reader.ReadUInt();
            Name     = reader.ReadString();
            uint customisationCount = reader.ReadUInt();
            for (uint i = 0; i < customisationCount; i++)
            {
                var slotId = reader.ReadUInt();
                var optionId = reader.ReadUInt();
                var tintId = reader.ReadUInt();

                CustomisationOptions.Add(new(slotId, optionId, tintId));
            }

            SkinTint = reader.ReadUInt();
            MaterialOverride = reader.ReadString();
            Race     = (Race)reader.ReadUInt();
            Gender   = (Gender)reader.ReadUInt();
            StartingOutfit = reader.ReadUInt();
        }
    }

    /// <summary>
    /// The 0.589 form (login protocol 9). Its request builder is the same function in both clients (0.589
    /// FUN_1439a2be0 / FUN_143006f60 = 0.695 FUN_143a5aac0 / FUN_1430ad2f0, trace/0695/functions.tsv).
    /// </summary>
    [AuthMessage(AuthMessageOpcode.CharacterCreateRequest, ProtocolVersion.LoginUdp_9)]
    public class CharacterCreateRequest9 : IReadable
    {
        public ulong ServerId { get; set; }
        /// <summary>Byte count of the payload that follows [P], as in <see cref="CharacterCreateRequest"/>.</summary>
        public uint PayloadLength { get; set; }
        public byte EmpireId { get; set; }
        public uint ProfileTypeId { get; set; }
        public uint Gender { get; set; }
        public string Name { get; set; }
        public List<(uint, uint, uint)> CustomisationOptions { get; set; } = new();
        public uint SkinTint { get; set; }

        public void Read(GamePacketReader reader)
        {
            ServerId = reader.ReadULong();
            PayloadLength = reader.ReadUInt();
            EmpireId = reader.ReadByte();
            ProfileTypeId = reader.ReadUInt();
            Gender   = reader.ReadUInt();
            Name     = reader.ReadString();

            uint customisationCount = reader.ReadUInt();
            for (uint i = 0; i < customisationCount; i++)
            {
                var slotId = reader.ReadUInt();
                var optionId = reader.ReadUInt();
                var tintId = reader.ReadUInt();

                CustomisationOptions.Add(new (slotId, optionId, tintId));
            }
            
            SkinTint = reader.ReadUInt();
        }
    }
}
