using LandmarkEmulator.AuthServer.Network.Message.Static;
using LandmarkEmulator.Shared.Network;
using System.Collections.Generic;

namespace LandmarkEmulator.AuthServer.Network.Message.Model.TunnelData
{
    /// <summary>
    /// Login tunnel type 9: the account's build sites per world, for the server list's "Claims" column.
    /// <para>
    /// 0.695 client [P]: login tunnel switch FUN_1430b5370 case 9 → FUN_1430acc40 (a
    /// <c>SoeUtil::List&lt;ServerClaimBasicData&gt;</c>) → reader FUN_1430af120 (u32 count, then FUN_1430addc0 per
    /// record) → handler (label 1430b2a70) → FUN_1430c4390: replaces the list at login state +0x20 +0x260 and rebuilds
    /// the server list (FUN_1430c4a30, Lua "GameEvents:ServerListUpdated"). Only two fields are ever read:
    /// <list type="bullet">
    /// <item>the server list row builder FUN_1430c3840 counts the records whose <see cref="Claim.ServerId"/> is the row's
    /// server and sets the row's "Claims" field (string at 0x14469f454);</item>
    /// <item>a successful character delete (FUN_1430b48f0 → FUN_1430c2400 → FUN_1430c2b20) removes the records whose
    /// <see cref="Claim.OwnerCharacterId"/> is the deleted character, then rebuilds the list.</item>
    /// </list>
    /// No other code reads the list (its only users are the clear / copy / remove helpers FUN_1430b6090, FUN_1430b7510,
    /// FUN_1430b5f70), so the rest of each record is stored and never read [P].
    /// </para>
    /// <para>
    /// The count also shows in the Teleport Spire's world list (TeleportSpireWidget WorldListItemRenderer / WorldDropdownMenu
    /// "Claims"). Sent empty for now, so every world shows 0 claims. Live filled it with the account's sites; doing that
    /// needs the claims of every world the account has characters on (open lead, docs/unknowns.md "Login and auth").
    /// </para>
    /// </summary>
    [TunnelData(TunnelDataType.ClaimData)]
    public class ClaimData : ITunnelData
    {
        /// <summary>One <c>ServerClaimBasicData</c> (0x78 bytes, filled in wire order by FUN_1430addc0).</summary>
        public class Claim : ITunnelData
        {
            /// <summary>+0x00, u64: stored, never read [P]. The site's claim id by its place before the owner [I].</summary>
            public ulong ClaimId { get; set; }
            /// <summary>+0x08, u64 [P]: the owning character; a character delete drops its records (FUN_1430c2b20).</summary>
            public ulong OwnerCharacterId { get; set; }
            /// <summary>+0x10, u64: stored, never read [P].</summary>
            public ulong Field10 { get; set; }
            /// <summary>+0x18, u32 [P]: the world (server id) the site is on; counted per server list row (FUN_1430c3840).</summary>
            public uint ServerId { get; set; }
            /// <summary>+0x1c, u32: stored, never read [P].</summary>
            public uint Field1C { get; set; }
            /// <summary>+0x20, u32: stored, never read [P].</summary>
            public uint Field20 { get; set; }
            /// <summary>+0x24, bool: stored, never read [P].</summary>
            public bool Flag24 { get; set; }
            /// <summary>+0x28, string: stored, never read [P]. The site name by analogy with the zone claim record [I].</summary>
            public string Name { get; set; } = "";
            /// <summary>+0x40, 3 × 8 bytes: stored, never read [P]. The site's minimum corner (f64) by analogy [I].</summary>
            public double[] Min { get; set; } = new double[3];
            /// <summary>+0x58, 3 × 8 bytes: stored, never read [P]. The site's maximum corner (f64) by analogy [I].</summary>
            public double[] Max { get; set; } = new double[3];
            /// <summary>+0x70, u32: stored, never read [P].</summary>
            public uint Field70 { get; set; }
            /// <summary>+0x74, u32: stored, never read [P].</summary>
            public uint Field74 { get; set; }

            public uint GetSize() => (uint)(8 + 8 + 8 + 4 + 4 + 4 + 1 + 4 + Name.Length + 3 * 8 + 3 * 8 + 4 + 4);

            public void Read(GamePacketReader reader)
            {
                ClaimId = reader.ReadULong();
                OwnerCharacterId = reader.ReadULong();
                Field10 = reader.ReadULong();
                ServerId = reader.ReadUInt();
                Field1C = reader.ReadUInt();
                Field20 = reader.ReadUInt();
                Flag24 = reader.ReadBool();
                Name = reader.ReadString();
                for (int i = 0; i < Min.Length; i++)
                    Min[i] = reader.ReadDouble();
                for (int i = 0; i < Max.Length; i++)
                    Max[i] = reader.ReadDouble();
                Field70 = reader.ReadUInt();
                Field74 = reader.ReadUInt();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(ClaimId);
                writer.Write(OwnerCharacterId);
                writer.Write(Field10);
                writer.Write(ServerId);
                writer.Write(Field1C);
                writer.Write(Field20);
                writer.Write(Flag24);
                writer.Write(Name);
                for (int i = 0; i < Min.Length; i++)
                    writer.Write(Min[i]);
                for (int i = 0; i < Max.Length; i++)
                    writer.Write(Max[i]);
                writer.Write(Field70);
                writer.Write(Field74);
            }
        }

        public List<Claim> Claims { get; set; } = new();

        public uint GetSize()
        {
            uint size = 4u;
            Claims.ForEach(i => size += i.GetSize());
            return size;
        }

        public void Read(GamePacketReader reader)
        {
            Claims.Clear();
            uint count = reader.ReadUInt();
            for (uint i = 0; i < count; i++)
            {
                // Was "Claims.Add(new Claim())" without reading the record, which left the rest of the body unread.
                var claim = new Claim();
                claim.Read(reader);
                Claims.Add(claim);
            }
        }

        public void Write(GamePacketWriter writer)
        {
            writer.Write((uint)Claims.Count);
            Claims.ForEach(i => i.Write(writer));
        }
    }
}
