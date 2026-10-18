using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model.Shared
{
    /// <summary>
    /// The client's PacketMountInfo (List&lt;PacketMountInfo&gt; RTTI), element reader 0.695 FUN_142c23fe0. Used by the
    /// SendSelfToClient mount list (copied into the mount manager at player +0x59a00, a HashListMap&lt;int,
    /// MountClientInfo&gt;, by FUN_142f0fd70) and by PacketMountList (MountBase 0xA1 sub 5 / 9, FUN_142f0e4f0). Wire
    /// order: u32 +0x0, u32 +0x4, u32 +0x8, u64 +0x28, bool +0x30, u32 +0xc, string +0x10 (MountClientInfo offsets).
    /// Landmark had no mounts: the live server sent the list empty, and so does this server.
    /// "never read [P]" = only the reader and the copy FUN_142f0fd70 touch it.
    /// </summary>
    public class Mount : IReadable, IWritable
    {
        /// <summary>
        /// +0x0 mount id: the manager's key; Lua Mount.Spawn (FUN_1435ef530) stores the id it asks for at manager +0xa8,
        /// and an arriving actor is auto-mounted when its guid is <see cref="MountGuid"/> of the entry with that id
        /// (FUN_142f0fce0, called from FUN_142eac160) [P].
        /// </summary>
        public uint MountId { get; set; }
        /// <summary>+0x4 u32. Never read [P].</summary>
        public uint Slot04 { get; set; }
        /// <summary>+0x8 u32. Never read [P].</summary>
        public uint Slot08 { get; set; }
        /// <summary>+0x28 the mount actor's guid, compared with a spawned actor's guid for the auto-mount (FUN_142f0fce0) [P].</summary>
        public ulong MountGuid { get; set; }
        /// <summary>+0x30 bool. Never read [P].</summary>
        public bool Slot30 { get; set; }
        /// <summary>+0xc u32. Never read [P].</summary>
        public uint Slot0C { get; set; }
        /// <summary>+0x10 string. Never read [P].</summary>
        public string Slot10 { get; set; } = "";

        public void Read(GamePacketReader reader)
        {
            MountId = reader.ReadUInt();
            Slot04 = reader.ReadUInt();
            Slot08 = reader.ReadUInt();
            MountGuid = reader.ReadULong();
            Slot30 = reader.ReadBool();
            Slot0C = reader.ReadUInt();
            Slot10 = reader.ReadString();
        }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(MountId);
            writer.Write(Slot04);
            writer.Write(Slot08);
            writer.Write(MountGuid);
            writer.Write(Slot30);
            writer.Write(Slot0C);
            writer.Write(Slot10);
        }
    }
}
