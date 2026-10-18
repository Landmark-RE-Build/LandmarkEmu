using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// ClientGameSettings 0x8E (S→C): zone-wide game settings. 0.695 reader FUN_142c236c0: u32 +0x10, u32 +0x14,
    /// u32 +0x18, bool +0x1c, then six 4-byte fields +0x20..+0x34; handled by the zone dispatcher FUN_142d1ccf0 case 0x3b
    /// (disassembled at 0x142d1f305..0x142d1f33e for the float argument Ghidra dropped) [P]. The server sends the live
    /// capture's 37 bytes as they are (WorldSession, zone entry); this model names them. Until 2026-10-18 the model had
    /// five u32 before the bool (the bool is the fourth field) and named nothing; the bytes sent never changed.
    /// Live values: 0, 10, 0, false, 1.0, 0, 1, 0, 12.0, 110.0.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ClientGameSettings)]
    public class ClientGameSettings : IReadable, IWritable
    {
        /// <summary>The live capture's body, sent at zone entry (decodes to the values in the summary).</summary>
        public static readonly byte[] LiveBody = System.Convert.FromHexString(
            "00000000" + "0A000000" + "00000000" + "00" + "0000803F" + "00000000" + "01000000" + "00000000" + "00004041" + "0000DC42");

        /// <summary>+0x10 → game +0x326cc; client stores at game +0x326cc, never read [P] (only this handler touches it). Live 0.</summary>
        public uint Slot10 { get; set; }
        /// <summary>
        /// +0x14 → game +0x326d0 (constructor default 8): the default interaction range in metres, used as an int → float
        /// wherever an entity has no range of its own — a new NPC / prop's interaction range (FUN_142cde1c0 → entity
        /// +0x4118) and its interaction sphere radius (FUN_142ea4230, entity +0x411c) [P use; I name]. Live 10.
        /// </summary>
        public uint DefaultInteractionRange { get; set; }
        /// <summary>+0x18 → game +0x326d4; client stores at game +0x326d4, never read [P]. Live 0.</summary>
        public uint Slot18 { get; set; }
        /// <summary>
        /// +0x1C → game +0x326d8: turns on an extra movement check in the character physics (FUN_142e98e90: a projected
        /// distance test on the actor's velocity before it may continue) [P use; I name]. Live false.
        /// </summary>
        public bool MovementCheck { get; set; }
        /// <summary>
        /// +0x20 the game time scalar, f32 → game +0x32878 and Lua GameEvents:OnSetGameTimeScalar (FUN_142d4f050, xmm1 at
        /// 0x142d1f32e) [P]. Live 1.0. (Was "never read" in docs/unknowns.md: the decompile had lost the float argument.)
        /// </summary>
        public float GameTimeScalar { get; set; }
        /// <summary>
        /// +0x24 → DAT_1452ecd28 on the first ClientGameSettings of the process only (FUN_143b90b60 latch): the
        /// equipment store's lookup mode — 0 = one equipment record per profile id ("No equipment record for profileId=%d",
        /// FUN_142e42560 and the equipment packet handlers), non-zero = one shared record for every profile [P use; I name].
        /// Live 0.
        /// </summary>
        public uint SharedEquipmentRecord { get; set; }
        /// <summary>
        /// +0x28 → DAT_1452ecdc8, latched like +0x24 (FUN_143b94510): the experience store's lookup mode — 0 = per profile,
        /// non-zero = one shared record (FUN_142cd2150 rank lookup, FUN_142ed77a0 Experience packets) [P use; I name].
        /// Live 1: one experience record for all profiles.
        /// </summary>
        public uint SharedExperienceRecord { get; set; }
        /// <summary>+0x2C → DAT_1455fd044 (with +0x28, FUN_143b94510); written only, never read [P]. Live 0.</summary>
        public uint Slot2C { get; set; }
        /// <summary>
        /// +0x30 → game +0x326dc (constructor 10.0; getter FUN_142ccd6d0): the impact speed above which a character's
        /// contact with a physics actor counts as a hit (FUN_142e6e830; also scaled into an animation parameter in
        /// FUN_142ec5ac0) [P use; I name]. Live 12.0.
        /// </summary>
        public float ImpactSpeedThreshold { get; set; }
        /// <summary>
        /// +0x34 → game +0x326e0 (getter FUN_142ccd6b0): the scale of that impact, (speed − threshold) × scale
        /// (FUN_142e6e830, which then calls the actor's vtable +0x170) [P use; I name]. Live 110.0.
        /// </summary>
        public float ImpactScale { get; set; }

        public void Read(GamePacketReader reader)
        {
            Slot10 = reader.ReadUInt();
            DefaultInteractionRange = reader.ReadUInt();
            Slot18 = reader.ReadUInt();
            MovementCheck = reader.ReadBool();
            GameTimeScalar = reader.ReadSingle();
            SharedEquipmentRecord = reader.ReadUInt();
            SharedExperienceRecord = reader.ReadUInt();
            Slot2C = reader.ReadUInt();
            ImpactSpeedThreshold = reader.ReadSingle();
            ImpactScale = reader.ReadSingle();
        }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(Slot10);
            writer.Write(DefaultInteractionRange);
            writer.Write(Slot18);
            writer.Write(MovementCheck);
            writer.Write(GameTimeScalar);
            writer.Write(SharedEquipmentRecord);
            writer.Write(SharedExperienceRecord);
            writer.Write(Slot2C);
            writer.Write(ImpactSpeedThreshold);
            writer.Write(ImpactScale);
        }
    }
}
