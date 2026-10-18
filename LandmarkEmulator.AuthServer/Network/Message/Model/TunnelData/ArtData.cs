using LandmarkEmulator.AuthServer.Network.Message.Static;
using LandmarkEmulator.Shared.Network;
using System.Collections.Generic;

namespace LandmarkEmulator.AuthServer.Network.Message.Model.TunnelData
{
    /// <summary>
    /// Login tunnel type 10: the tint definitions (the client's <c>TintDefinitionManagerData</c>; the zone sends the same
    /// body as ArtData 0x116 sub 1, WorldServer Game/ArtTints.cs). Every field below is named from the 0.695 client [P].
    /// <para>
    /// Path: login tunnel switch FUN_1430b5370 case 10 → FUN_1430acd60 → reader FUN_1430af1e0 (eight lists, in this
    /// order: FUN_142c36820, FUN_142c351d0, FUN_142c355a0, FUN_142c3fe00, FUN_142c3bac0 ×2, FUN_142c37f00, FUN_142c351d0) →
    /// handler (label 1430b2a90) → FUN_142d08480 → FUN_143cd0780, which REPLACES the lists of the client's
    /// TintDefinitionManager (DAT_1454313b8) with the packet's. The meaning of each list comes from the client's own
    /// builder of the same lists from local tables, ToolTintDefinitionManager FUN_143cd0ef0 (TintSemanticTables.txt,
    /// ArtTintOverrideGroups / Entries, ArtSourceActorTintTypes, ArtSourceActors, ArtTintTypeTints, ArtTintTypes), and
    /// from the lookups that read them (FUN_143ccf6f0 alias by hash + slot, FUN_143ccf800 override by group + actor).
    /// </para>
    /// <para>
    /// Hashes are the client's string hash (one-at-a-time with signed shifts: <c>h=(h+c)*0x401; h^=h&gt;&gt;6</c>, then
    /// <c>h*=9; h^=h&gt;&gt;11; h*=0x8001</c>). Some are taken of the upper-cased string (FUN_1437ebf3c per character);
    /// each field says which. All values come from Resources/ArtTints.json, a live capture that matches the 0.695
    /// ArtTints.txt (trace/0695/agent-leads2-b.md L103), so nothing here is invented. About 736 KB on the wire.
    /// </para>
    /// </summary>
    [TunnelData(TunnelDataType.ArtData)]
    public class ArtData : ITunnelData
    {
        /// <summary>
        /// List 1 (reader FUN_142c36820) → manager +0x878: alias hash → alias name (FUN_143cca6a0). The local builder adds
        /// one per TintSemanticTables alias (FUN_143cca5c0).
        /// </summary>
        public class AliasNameEntry : ITunnelData
        {
            /// <summary>Map key: hash of ALIAS_NAME as written (no case folding).</summary>
            public int AliasHash { get; set; }
            /// <summary>The ArtTints alias, e.g. "Generic_Red".</summary>
            public string AliasName { get; set; } = "";

            public uint GetSize() => (uint)(4u + 4u + AliasName.Length);

            public void Read(GamePacketReader reader)
            {
                AliasHash = reader.ReadInt();
                AliasName = reader.ReadString();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(AliasHash);
                writer.Write(AliasName);
            }
        }

        /// <summary>
        /// List 2 (reader FUN_142c351d0: key first, then the value) → manager +0x18e8: ArtTints.txt row id → alias hash.
        /// The handler also rebuilds the reverse map +0x2958 (hash → id) from it. FUN_143ccf800 turns an override's
        /// ArtTintId into the alias hash through this map.
        /// </summary>
        public class AliasIdEntry : ITunnelData
        {
            /// <summary>Map key: the ArtTints.txt ID (TintSemanticTables column 2), e.g. 9 for Generic_Red.</summary>
            public int AliasId { get; set; }
            /// <summary>Hash of the alias name.</summary>
            public int AliasHash { get; set; }

            public uint GetSize() => 8u;

            public void Read(GamePacketReader reader)
            {
                AliasId = reader.ReadInt();
                AliasHash = reader.ReadInt();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(AliasId);
                writer.Write(AliasHash);
            }
        }

        /// <summary>
        /// List 3 (reader FUN_142c355a0, entries FUN_142c38770) → manager +0x39c8: ArtTintOverrideGroups.txt with their
        /// ArtTintOverrideEntries.txt rows. FUN_143ccf800 (caller FUN_143d9e610) finds a group by its key, finds the
        /// actor's tint type (<see cref="ActorTintType"/>) and returns the alias of the entry with that tint type.
        /// </summary>
        public class OverrideGroup : ITunnelData
        {
            /// <summary>One ArtTintOverrideEntries.txt row: ART_TINT_TYPE_ID (row +0x14) and ART_TINT_ID (row +0x18).</summary>
            public class OverrideEntry : ITunnelData
            {
                public int ArtTintTypeId { get; set; }
                /// <summary>ArtTints.txt id of the alias to use instead (looked up in <see cref="AliasIds"/>).</summary>
                public int ArtTintId { get; set; }

                public uint GetSize() => 8u;

                public void Read(GamePacketReader reader)
                {
                    ArtTintTypeId = reader.ReadInt();
                    ArtTintId = reader.ReadInt();
                }

                public void Write(GamePacketWriter writer)
                {
                    writer.Write(ArtTintTypeId);
                    writer.Write(ArtTintId);
                }
            }

            /// <summary>Map key (record +0x58): the group id (ART_TINT_OVERRIDE_GROUP_ID).</summary>
            public int Key { get; set; }
            /// <summary>
            /// Record +0x0: the group row's ART_SOURCE_ACTOR_ID in the local builder. The live capture has 0 in all four
            /// groups (the 0.695 table says 384 / 484 / 425 / ...); kept as captured. The handler copies it
            /// (FUN_143ccc5e0) and the lookup FUN_143ccf800 does not read it; no other reader found yet.
            /// </summary>
            public int ArtSourceActorId { get; set; }
            /// <summary>Record +0x4: the group id again (the local builder stores the entry row's group id here too).</summary>
            public int GroupId { get; set; }
            public List<OverrideEntry> Entries { get; set; } = new();

            public uint GetSize()
            {
                uint size = 4u + 4u + 4u + 4u;
                Entries.ForEach(x => size += x.GetSize());
                return size;
            }

            public void Read(GamePacketReader reader)
            {
                Key = reader.ReadInt();
                ArtSourceActorId = reader.ReadInt();
                GroupId = reader.ReadInt();

                Entries.Clear();
                uint count = reader.ReadUInt();
                for (uint i = 0; i < count; i++)
                {
                    var entry = new OverrideEntry();
                    entry.Read(reader);
                    Entries.Add(entry);
                }
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(Key);
                writer.Write(ArtSourceActorId);
                writer.Write(GroupId);
                writer.Write((uint)Entries.Count);
                Entries.ForEach(x => x.Write(writer));
            }
        }

        /// <summary>
        /// List 4 (reader FUN_142c3fe00, record FUN_142c17050) → manager +0x39f8: one per ArtSourceActorTintTypes.txt row,
        /// keyed by model + shader so FUN_143ccf800 can find the tint type of a mesh's material.
        /// </summary>
        public class ActorTintType : ITunnelData
        {
            /// <summary>Map key (record +0x20): <c>((long)ActorModelHash &lt;&lt; 32) + ShaderNameHash</c> (signed add).</summary>
            public long Key { get; set; }
            /// <summary>Record +0x0: ART_TINT_TYPE_ID (row +0x14), e.g. 607.</summary>
            public int ArtTintTypeId { get; set; }
            /// <summary>
            /// Record +0x4: hash of the ArtSourceActors.txt ACTOR_FILE_NAME made into a model name by FUN_143cce9a0
            /// (drops "_LOD"/"Auto", ".adr" → ".dme"), e.g. Char_Biped_HumanFemale_Entities_Victorian_000_Chest.dme.
            /// </summary>
            public int ActorModelHash { get; set; }
            /// <summary>Record +0x8: hash of SHADER_NAME (row +0x18), e.g. HumanFemale_Body_000.</summary>
            public int ShaderNameHash { get; set; }
            /// <summary>Record +0xc: ART_SOURCE_ACTOR_ID (row +0x10).</summary>
            public int ArtSourceActorId { get; set; }

            public uint GetSize() => 8u + 4u + 4u + 4u + 4u;

            public void Read(GamePacketReader reader)
            {
                Key = reader.ReadLong();
                ArtTintTypeId = reader.ReadInt();
                ActorModelHash = reader.ReadInt();
                ShaderNameHash = reader.ReadInt();
                ArtSourceActorId = reader.ReadInt();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(Key);
                writer.Write(ArtTintTypeId);
                writer.Write(ActorModelHash);
                writer.Write(ShaderNameHash);
                writer.Write(ArtSourceActorId);
            }
        }

        /// <summary>
        /// One TintSemanticTables.txt row of an alias (reader FUN_142c2ff10, a 0x60-byte TintSemantic). The local builder
        /// FUN_143cd0ef0 fills it from the columns SEMANTIC_GROUP, SEMANTIC_NAME, EDIT_TYPE, STRING_PARAM, FLOAT_1..4.
        /// </summary>
        public class TintSemantic : ITunnelData
        {
            /// <summary>+0x00: SEMANTIC_GROUP, e.g. "BaseTint", "TilingTint", "DecalTint".</summary>
            public string SemanticGroup { get; set; } = "";
            /// <summary>+0x18: hash of SEMANTIC_GROUP upper-cased.</summary>
            public int SemanticGroupHash { get; set; }
            /// <summary>+0x1c: hash of SEMANTIC_NAME as written (e.g. BaseTintHighlightA = b89339c1).</summary>
            public int SemanticNameHash { get; set; }
            /// <summary>+0x20: hash of EDIT_TYPE upper-cased (RGB, SCALAR, TEXTURE, ENABLE, VECTOR2).</summary>
            public int EditTypeHash { get; set; }
            /// <summary>+0x28: STRING_PARAM, e.g. a texture name ("white.dds") for a TEXTURE semantic; mostly empty.</summary>
            public string StringParam { get; set; } = "";
            /// <summary>+0x40: hash of STRING_PARAM upper-cased (0 when empty).</summary>
            public int StringParamHash { get; set; }
            /// <summary>
            /// +0x50: FLOAT_1..FLOAT_4 (r, g, b, a for an RGB semantic). The local builder forces the 4th to 0.5 for one
            /// group / edit type pair (DAT_14560342c / DAT_145603438).
            /// </summary>
            public float[] Values { get; set; } = new float[4];

            public uint GetSize() => (uint)(4u + SemanticGroup.Length + 4u + 4u + 4u + 4u + StringParam.Length + 4u + 16u);

            public void Read(GamePacketReader reader)
            {
                SemanticGroup = reader.ReadString();
                SemanticGroupHash = reader.ReadInt();
                SemanticNameHash = reader.ReadInt();
                EditTypeHash = reader.ReadInt();
                StringParam = reader.ReadString();
                StringParamHash = reader.ReadInt();
                Values = new float[4];
                for (int i = 0; i < Values.Length; i++)
                    Values[i] = reader.ReadSingle();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(SemanticGroup);
                writer.Write(SemanticGroupHash);
                writer.Write(SemanticNameHash);
                writer.Write(EditTypeHash);
                writer.Write(StringParam);
                writer.Write(StringParamHash);
                for (int i = 0; i < 4; i++)
                    writer.Write(Values != null && i < Values.Length ? Values[i] : 0f);
            }
        }

        /// <summary>
        /// List 7 (reader FUN_142c37f00: key, then FUN_142c30030) → manager +0x8: the aliases (0x280-byte record) that
        /// every tint lookup uses: FUN_143ccf6f0 matches +0x268 (alias hash) and +0x26c (slot hash), falling back to the
        /// alias's "ANY" slot. Prop tints (PlayerUpdate 0x7A), item and character default tints all resolve here.
        /// </summary>
        public class TintAlias : ITunnelData
        {
            /// <summary>Map key (record +0x280): hash of ALIAS_NAME as written.</summary>
            public int Key { get; set; }
            /// <summary>+0x00: ALIAS_NAME.</summary>
            public string AliasName { get; set; } = "";
            /// <summary>+0x60: SLOT ("ANY" in every live row).</summary>
            public string Slot { get; set; } = "";
            /// <summary>+0xc0: the semantic rows.</summary>
            public List<TintSemantic> Semantics { get; set; } = new();
            /// <summary>+0x268: hash of ALIAS_NAME (the same value as <see cref="Key"/>).</summary>
            public int AliasHash { get; set; }
            /// <summary>+0x26c: hash of SLOT as written ("ANY" = 86a9418d).</summary>
            public int SlotHash { get; set; }

            public uint GetSize()
            {
                uint size = (uint)(4u + 4u + AliasName.Length + 4u + Slot.Length + 4u + 4u + 4u);
                Semantics.ForEach(x => size += x.GetSize());
                return size;
            }

            public void Read(GamePacketReader reader)
            {
                Key = reader.ReadInt();
                AliasName = reader.ReadString();
                Slot = reader.ReadString();

                Semantics.Clear();
                uint count = reader.ReadUInt();
                for (uint i = 0; i < count; i++)
                {
                    var semantic = new TintSemantic();
                    semantic.Read(reader);
                    Semantics.Add(semantic);
                }

                AliasHash = reader.ReadInt();
                SlotHash = reader.ReadInt();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(Key);
                writer.Write(AliasName);
                writer.Write(Slot);
                writer.Write((uint)Semantics.Count);
                Semantics.ForEach(x => x.Write(writer));
                writer.Write(AliasHash);
                writer.Write(SlotHash);
            }
        }

        /// <summary>
        /// List 8 (reader FUN_142c351d0: key, then value) → manager +0x3a28: the tints a player may pick. The local
        /// builder adds every ArtTintTypeTints.txt row whose ART_TINT_TYPE_ID belongs to an ArtTintTypes.txt row with
        /// PLAYER_TINT_OPTION = 1 (type 633 only: 64 tints, ART_TINT_ID 1000 and up).
        /// </summary>
        public class PlayerTintOption : ITunnelData
        {
            /// <summary>Key: ART_TINT_TYPE_ID (row +0x10).</summary>
            public int ArtTintTypeId { get; set; }
            /// <summary>ART_TINT_ID (row +0x14).</summary>
            public int ArtTintId { get; set; }

            public uint GetSize() => 8u;

            public void Read(GamePacketReader reader)
            {
                ArtTintTypeId = reader.ReadInt();
                ArtTintId = reader.ReadInt();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(ArtTintTypeId);
                writer.Write(ArtTintId);
            }
        }

        public List<AliasNameEntry> AliasNames { get; set; } = new();
        public List<AliasIdEntry> AliasIds { get; set; } = new();
        public List<OverrideGroup> OverrideGroups { get; set; } = new();
        public List<ActorTintType> ActorTintTypes { get; set; } = new();
        /// <summary>
        /// List 5 (reader FUN_142c3bac0) → manager +0x230: the distinct SEMANTIC_NAMEs of the "Default" alias (hash
        /// ea2ab532), in table order (BaseTintHighlightA, BaseTintMidtoneA, ...).
        /// </summary>
        public List<string> SemanticNames { get; set; } = new();
        /// <summary>List 6 (reader FUN_142c3bac0) → manager +0x550: the distinct EDIT_TYPEs of the "Default" alias.</summary>
        public List<string> EditTypes { get; set; } = new();
        public List<TintAlias> Aliases { get; set; } = new();
        public List<PlayerTintOption> PlayerTintOptions { get; set; } = new();

        public uint GetSize()
        {
            uint size = 8u * 4u;
            AliasNames.ForEach(x => size += x.GetSize());
            AliasIds.ForEach(x => size += x.GetSize());
            OverrideGroups.ForEach(x => size += x.GetSize());
            ActorTintTypes.ForEach(x => size += x.GetSize());
            SemanticNames.ForEach(x => size += 4u + (uint)x.Length);
            EditTypes.ForEach(x => size += 4u + (uint)x.Length);
            Aliases.ForEach(x => size += x.GetSize());
            PlayerTintOptions.ForEach(x => size += x.GetSize());
            return size;
        }

        private static void ReadList<T>(GamePacketReader reader, List<T> list) where T : ITunnelData, new()
        {
            list.Clear();
            uint count = reader.ReadUInt();
            for (uint i = 0; i < count; i++)
            {
                var item = new T();
                item.Read(reader);
                list.Add(item);
            }
        }

        private static void ReadStrings(GamePacketReader reader, List<string> list)
        {
            list.Clear();
            uint count = reader.ReadUInt();
            for (uint i = 0; i < count; i++)
                list.Add(reader.ReadString());
        }

        private static void WriteList<T>(GamePacketWriter writer, List<T> list) where T : ITunnelData
        {
            writer.Write((uint)list.Count);
            list.ForEach(x => x.Write(writer));
        }

        private static void WriteStrings(GamePacketWriter writer, List<string> list)
        {
            writer.Write((uint)list.Count);
            list.ForEach(writer.Write);
        }

        public void Read(GamePacketReader reader)
        {
            ReadList(reader, AliasNames);
            ReadList(reader, AliasIds);
            ReadList(reader, OverrideGroups);
            ReadList(reader, ActorTintTypes);
            ReadStrings(reader, SemanticNames);
            ReadStrings(reader, EditTypes);
            ReadList(reader, Aliases);
            ReadList(reader, PlayerTintOptions);
        }

        public void Write(GamePacketWriter writer)
        {
            WriteList(writer, AliasNames);
            WriteList(writer, AliasIds);
            WriteList(writer, OverrideGroups);
            WriteList(writer, ActorTintTypes);
            WriteStrings(writer, SemanticNames);
            WriteStrings(writer, EditTypes);
            WriteList(writer, Aliases);
            WriteList(writer, PlayerTintOptions);
        }
    }
}
