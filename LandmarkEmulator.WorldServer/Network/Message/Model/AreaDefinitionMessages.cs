using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    // Family 0xEA AreaDefinition: trigger volumes ("areas") made with the Area Tool (ToolTypes row 4 "TriggerVolume").
    // Layouts from the 0.695 client; the full trace with proof marks is trace/0695/area-definitions-695.md.
    //
    // Header (BaseAreaDefinitionPacket, reader FUN_1432f7fd0): u16 opcode 0xEA, u32 sub, u64 data set, bool. The
    // server's enum value 0xEA00xx00 covers the u16 and the low half of the sub, so each body here starts with the sub's
    // high half (u16 0), then the u64 data set (the client always sends 0) and the bool (0).

    /// <summary>
    /// One shape of an area (AreaShapeDefinition, 0x68 bytes in the client; reader FUN_1432f7e40, writer FUN_14319e570).
    /// The client's data-table loader of the same struct (FUN_143db7970, GlobalAreaShapes.txt) names its columns: ID
    /// +0x08, AREA_DEFINITION +0x0c, SHAPE_TYPE +0x18, SHAPE_PARAM_1-6 +0x20..+0x48, X/Y/Z_ROTATION +0x50/+0x54/+0x58 [P].
    /// </summary>
    public sealed class AreaShapeRecord
    {
        /// <summary>Shape type of AreaShapeDataBox (FUN_143de1100).</summary>
        public const uint TypeBox = 2;
        /// <summary>Shape type of AreaShapeDataSphere (FUN_143de1110).</summary>
        public const uint TypeSphere = 1;

        /// <summary>+0x08 the shape's static definition id (GlobalAreaShapes.txt ID) [P]; 0 for shapes made in play.</summary>
        public uint DefinitionId { get; set; }
        /// <summary>
        /// +0x0C the static area definition the shape belongs to (GlobalAreaShapes.txt AREA_DEFINITION) [P]; 0 for shapes
        /// made in play (the tool's own, and the server's).
        /// </summary>
        public uint AreaDefinitionId { get; set; }
        /// <summary>+0x10 the shape's own guid: TransferShape (Group / Ungroup) and Area.Remove name a shape by it.</summary>
        public ulong Guid { get; set; }
        /// <summary>+0x18 <see cref="TypeBox"/> or <see cref="TypeSphere"/>.</summary>
        public uint Type { get; set; } = TypeBox;
        /// <summary>+0x20 minimum corner (landscape-local metres when <see cref="Frame"/> is 0).</summary>
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MinZ { get; set; }
        /// <summary>+0x38 maximum corner.</summary>
        public double MaxX { get; set; }
        public double MaxY { get; set; }
        public double MaxZ { get; set; }
        /// <summary>
        /// +0x50 rotation in radians: X_ROTATION (pitch), +0x54 Y_ROTATION (yaw), +0x58 Z_ROTATION (roll), the column
        /// names of the shape loader FUN_143db7970 [P]; the tool fills them with the Euler split of its widget matrix
        /// (FUN_142e550d0). The client's own box test ignores them (FUN_143de11f0).
        /// </summary>
        public float Pitch { get; set; }
        public float Yaw { get; set; }
        public float Roll { get; set; }
        /// <summary>
        /// +0x5C a flag the constructor sets to 1 (FUN_143db5d10). Client stores at +0x5c, never read [P within the area
        /// code: only the reader, the writer, the constructor and the copies (FUN_143db5d70, FUN_143db7bd0, FUN_143db8320,
        /// FUN_143db8e20) touch it; the scan covered FUN_1431a8000-1431b2000, FUN_1432f7000-1432fb000 and
        /// FUN_143db0000-143de2000]. Sent as the constructor's 1.
        /// </summary>
        public bool Slot5C { get; set; } = true;
        /// <summary>+0x60 coordinate frame: 0 = min / max are landscape-local floats (all the tool sends).</summary>
        public uint Frame { get; set; }

        /// <summary>Bytes on the wire: 4 + 4 + 8 + 4 + 6 × 8 + 3 × 4 + 1 + 4.</summary>
        public const int WireSize = 85;

        public static AreaShapeRecord Read(GamePacketReader reader)
        {
            var s = new AreaShapeRecord
            {
                DefinitionId = reader.ReadUInt(),
                AreaDefinitionId = reader.ReadUInt(),
                Guid = reader.ReadULong(),
                Type = reader.ReadUInt(),
                MinX = reader.ReadDouble(),
                MinY = reader.ReadDouble(),
                MinZ = reader.ReadDouble(),
                MaxX = reader.ReadDouble(),
                MaxY = reader.ReadDouble(),
                MaxZ = reader.ReadDouble(),
                Pitch = reader.ReadSingle(),
                Yaw = reader.ReadSingle(),
                Roll = reader.ReadSingle(),
                Slot5C = reader.ReadBool(),
                Frame = reader.ReadUInt()
            };
            return s;
        }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(DefinitionId);
            writer.Write(AreaDefinitionId);
            writer.Write(Guid);
            writer.Write(Type);
            writer.Write(MinX);
            writer.Write(MinY);
            writer.Write(MinZ);
            writer.Write(MaxX);
            writer.Write(MaxY);
            writer.Write(MaxZ);
            writer.Write(Pitch);
            writer.Write(Yaw);
            writer.Write(Roll);
            writer.Write(Slot5C);
            writer.Write(Frame);
        }

        /// <summary>
        /// A usable shape: a known type, finite corners, max − min &gt; 0 on every axis (the client's own validity test,
        /// AreaShapeDataBox FUN_143de1150) and no side longer than <paramref name="maxSide"/> metres.
        /// </summary>
        public bool IsValid(double maxSide)
        {
            if (Type != TypeBox && Type != TypeSphere)
                return false;
            double[] all = { MinX, MinY, MinZ, MaxX, MaxY, MaxZ, Pitch, Yaw, Roll };
            foreach (double v in all)
                if (double.IsNaN(v) || double.IsInfinity(v))
                    return false;
            double dx = MaxX - MinX, dy = MaxY - MinY, dz = MaxZ - MinZ;
            return dx > 0 && dy > 0 && dz > 0 && dx <= maxSide && dy <= maxSide && dz <= maxSide;
        }

        public AreaShapeRecord Clone() => (AreaShapeRecord)MemberwiseClone();
    }

    /// <summary>
    /// One property of an area (AreaPropertyDefinition, 0x120 bytes; reader FUN_1432f7bb0). The columns match
    /// AreaProperties.txt (ID, AREA_DEFINITION_ID, TYPE_ID, PROPERTY_INDEX, INT / FLOAT / STRING params, EDITABLE_PROPERTY
    /// names); the client data source Design.TriggerVolumePropertyTypes.&lt;type&gt; fills "Int" params at +0x18 + 4n,
    /// "Float" at +0x50 + 4n and "String" at (n + 5) × 0x18 (FUN_1431ae820). Kept as sent so a round trip loses nothing.
    /// </summary>
    public sealed class AreaPropertyRecord
    {
        /// <summary>AreaPropertyDataBlank (FUN_143db3530): the property the Area Tool gives its own local copy.</summary>
        public const uint TypeBlank = 19;

        public uint Id { get; set; }
        public uint AreaDefinitionId { get; set; }
        public uint Type { get; set; }
        public uint Index { get; set; }
        public int[] Ints { get; set; } = new int[9];
        public ulong[] Guids { get; set; } = new ulong[2];
        public float[] Floats { get; set; } = new float[9];
        /// <summary>Three string parameters, then three editable property names.</summary>
        public string[] Strings { get; set; } = { "", "", "", "", "", "" };

        public static AreaPropertyRecord Read(GamePacketReader reader)
        {
            var p = new AreaPropertyRecord
            {
                Id = reader.ReadUInt(),
                AreaDefinitionId = reader.ReadUInt(),
                Type = reader.ReadUInt(),
                Index = reader.ReadUInt()
            };
            for (int i = 0; i < p.Ints.Length; i++)
                p.Ints[i] = reader.ReadInt();
            for (int i = 0; i < p.Guids.Length; i++)
                p.Guids[i] = reader.ReadULong();
            for (int i = 0; i < p.Floats.Length; i++)
                p.Floats[i] = reader.ReadSingle();
            for (int i = 0; i < p.Strings.Length; i++)
                p.Strings[i] = AreaDefinitionRecord.ReadBoundedString(reader);
            return p;
        }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(Id);
            writer.Write(AreaDefinitionId);
            writer.Write(Type);
            writer.Write(Index);
            foreach (int v in Ints)
                writer.Write(v);
            foreach (ulong v in Guids)
                writer.Write(v);
            foreach (float v in Floats)
                writer.Write(v);
            foreach (string s in Strings)
                writer.Write(s ?? "");
        }

        public AreaPropertyRecord Clone() => new()
        {
            Id = Id, AreaDefinitionId = AreaDefinitionId, Type = Type, Index = Index,
            Ints = (int[])Ints.Clone(), Guids = (ulong[])Guids.Clone(), Floats = (float[])Floats.Clone(),
            Strings = (string[])Strings.Clone()
        };
    }

    /// <summary>
    /// An area (AreaDefinition, 0x88 bytes in the client; reader FUN_1432f76a0, constructor FUN_143db7d80): an area guid,
    /// a name, its shapes and its properties. The client's data-table loader of the same struct (FUN_143db8c80,
    /// GlobalAreas.txt / Areas.txt) names most fields by their column: ID +0x08, DESCRIPTION +0x18, DATASET_ID +0x30,
    /// IS_GLOBAL +0x34, SCRIPT_EVENT_ON_ENTER_ID +0x3c, SCRIPT_EVENT_ON_EXIT_ID +0x40, SCRIPT_EVENT_ON_ENTER_NPC_ID +0x44,
    /// SCRIPT_EVENT_ON_EXIT_NPC_ID +0x48, PERIODIC_INTERVAL +0x80 (not on the wire) [P]. Areas made in play (the Area
    /// Tool's, the server's) carry 0 / false in all of them, which is what the server sends.
    /// </summary>
    public sealed class AreaDefinitionRecord
    {
        /// <summary>Longest string accepted from a client (names, property strings).</summary>
        public const int MaxStringBytes = 256;
        /// <summary>Most shapes / properties accepted in one record from a client.</summary>
        public const int MaxShapes = 16, MaxProperties = 16;

        /// <summary>+0x08 static definition id (data-table areas); 0 for areas made in play.</summary>
        public uint DefinitionId { get; set; }
        /// <summary>+0x10 the area guid (0 = none).</summary>
        public ulong Guid { get; set; }
        /// <summary>+0x18 name.</summary>
        public string Name { get; set; } = "";
        /// <summary>+0x30 DATASET_ID [P]: the data set the area belongs to (the packet header's data set; 0).</summary>
        public uint DataSetId { get; set; }
        /// <summary>+0x34 IS_GLOBAL [P] (the constructor's argument; false from the tool).</summary>
        public bool IsGlobal { get; set; }
        /// <summary>
        /// +0x35 a flag the constructor sets to 1 and +0x38 a u32 it sets to 0: client stores them, never read [P within
        /// the area code: only the reader, the constructors (FUN_143db7d80, FUN_143db7e00) and the copies (FUN_143db80d0,
        /// FUN_143db8810) touch them; same scan as <see cref="AreaShapeRecord.Slot5C"/>]. Sent as the constructor's values.
        /// </summary>
        public bool Slot35 { get; set; } = true;
        public uint Slot38 { get; set; }
        public List<AreaShapeRecord> Shapes { get; set; } = new();
        public List<AreaPropertyRecord> Properties { get; set; } = new();
        /// <summary>
        /// +0x3C / +0x40 / +0x44 / +0x48 the script events run when a player enters / leaves and an NPC enters / leaves
        /// (SCRIPT_EVENT_ON_ENTER_ID, _ON_EXIT_ID, _ON_ENTER_NPC_ID, _ON_EXIT_NPC_ID) [P names]; 0 = none.
        /// </summary>
        public uint ScriptEventOnEnterId { get; set; }
        public uint ScriptEventOnExitId { get; set; }
        public uint ScriptEventOnEnterNpcId { get; set; }
        public uint ScriptEventOnExitNpcId { get; set; }

        /// <summary>A string no longer than <see cref="MaxStringBytes"/> (a longer one is a malformed packet).</summary>
        internal static string ReadBoundedString(GamePacketReader reader)
        {
            uint length = reader.ReadUInt();
            if (length > MaxStringBytes)
                throw new InvalidOperationException($"Area string of {length} bytes.");
            return length == 0 ? "" : System.Text.Encoding.UTF8.GetString(reader.ReadBytes(length));
        }

        public static AreaDefinitionRecord Read(GamePacketReader reader)
        {
            var d = new AreaDefinitionRecord
            {
                DefinitionId = reader.ReadUInt(),
                Guid = reader.ReadULong(),
                Name = ReadBoundedString(reader),
                DataSetId = reader.ReadUInt(),
                IsGlobal = reader.ReadBool(),
                Slot35 = reader.ReadBool(),
                Slot38 = reader.ReadUInt()
            };
            uint shapes = reader.ReadUInt();
            if (shapes > MaxShapes)
                throw new InvalidOperationException($"Area with {shapes} shapes.");
            for (uint i = 0; i < shapes; i++)
                d.Shapes.Add(AreaShapeRecord.Read(reader));
            uint properties = reader.ReadUInt();
            if (properties > MaxProperties)
                throw new InvalidOperationException($"Area with {properties} properties.");
            for (uint i = 0; i < properties; i++)
                d.Properties.Add(AreaPropertyRecord.Read(reader));
            d.ScriptEventOnEnterId = reader.ReadUInt();
            d.ScriptEventOnExitId = reader.ReadUInt();
            d.ScriptEventOnEnterNpcId = reader.ReadUInt();
            d.ScriptEventOnExitNpcId = reader.ReadUInt();
            return d;
        }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(DefinitionId);
            writer.Write(Guid);
            writer.Write(Name ?? "");
            writer.Write(DataSetId);
            writer.Write(IsGlobal);
            writer.Write(Slot35);
            writer.Write(Slot38);
            writer.Write((uint)Shapes.Count);
            foreach (AreaShapeRecord s in Shapes)
                s.Write(writer);
            writer.Write((uint)Properties.Count);
            foreach (AreaPropertyRecord p in Properties)
                p.Write(writer);
            writer.Write(ScriptEventOnEnterId);
            writer.Write(ScriptEventOnExitId);
            writer.Write(ScriptEventOnEnterNpcId);
            writer.Write(ScriptEventOnExitNpcId);
        }

        /// <summary>The record alone, as stored in the database (<c>world_area.data</c>).</summary>
        public byte[] ToBytes()
        {
            var output = new List<byte>();
            Write(new GamePacketWriter(output));
            return output.ToArray();
        }

        public static AreaDefinitionRecord FromBytes(byte[] data) => Read(new GamePacketReader(data));

        public AreaDefinitionRecord Clone()
        {
            var copy = (AreaDefinitionRecord)MemberwiseClone();
            copy.Shapes = Shapes.ConvertAll(s => s.Clone());
            copy.Properties = Properties.ConvertAll(p => p.Clone());
            return copy;
        }
    }

    /// <summary>Reads the 0xEA header after the enum's bytes: sub high half, u64 data set, bool.</summary>
    internal static class AreaDefinitionHeader
    {
        public static (ulong DataSet, bool Flag) Read(GamePacketReader reader)
        {
            reader.ReadUShort();
            return (reader.ReadULong(), reader.ReadBool());
        }

        /// <summary>Writes the same header for a server message (data set 0, flag false).</summary>
        public static void Write(GamePacketWriter writer)
        {
            writer.Write((ushort)0);
            writer.Write(0ul);
            writer.Write(false);
        }
    }

    /// <summary>
    /// C→S AreaDefinition 7 CreateTriggerAreaRequest (builder FUN_1431a93e0, from the Area Tool FUN_143739100 / Lua
    /// Area.Create): the box the player drew (type 2, frame 0, rotation zero) and the client's pending request id, which
    /// the answer (<see cref="Game.AreaDefinitions"/> CreateTriggerAreaResponse, sub 8) must repeat.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.AreaDefinitionCreateTriggerAreaRequest)]
    public class AreaDefinitionCreateTriggerAreaRequest : IReadable
    {
        public ulong DataSet { get; private set; }
        public AreaShapeRecord Shape { get; private set; }
        public uint RequestId { get; private set; }

        public void Read(GamePacketReader reader)
        {
            (DataSet, _) = AreaDefinitionHeader.Read(reader);
            Shape = AreaShapeRecord.Read(reader);
            RequestId = reader.ReadUInt();
        }
    }

    /// <summary>
    /// C→S AreaDefinition 3 UpdateDefinition (builder FUN_1432fa5e0; Lua Area.SetName / AddProperty / RemoveProperty /
    /// SetPropertyValue(s) / Rotate, and Area.Remove of one of several shapes): the whole changed area, then a bool (0).
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.AreaDefinitionUpdateDefinition)]
    public class AreaDefinitionUpdateRequest : IReadable
    {
        public ulong DataSet { get; private set; }
        public AreaDefinitionRecord Area { get; private set; }

        public void Read(GamePacketReader reader)
        {
            (DataSet, _) = AreaDefinitionHeader.Read(reader);
            Area = AreaDefinitionRecord.Read(reader);
            if (reader.BytesRemaining > 0)
                reader.ReadBool();
        }
    }

    /// <summary>C→S AreaDefinition 4 RemoveDefinition (builder FUN_1432fa3f0; Area.Remove of an area's last shape).</summary>
    [ZoneMessage(ZoneMessageOpcode.AreaDefinitionRemoveDefinition)]
    public class AreaDefinitionRemoveRequest : IReadable
    {
        public ulong DataSet { get; private set; }
        public AreaDefinitionRecord Area { get; private set; }

        public void Read(GamePacketReader reader)
        {
            (DataSet, _) = AreaDefinitionHeader.Read(reader);
            Area = AreaDefinitionRecord.Read(reader);
        }
    }

    /// <summary>
    /// C→S AreaDefinition 2 TransferShape (builder FUN_1432fa4c0; Lua Area.Group / Area.Ungroup): area A (gets the
    /// shape), area B (gives it up), the shape's guid.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.AreaDefinitionTransferShape)]
    public class AreaDefinitionTransferShapeRequest : IReadable
    {
        public ulong DataSet { get; private set; }
        public AreaDefinitionRecord To { get; private set; }
        public AreaDefinitionRecord From { get; private set; }
        public ulong ShapeGuid { get; private set; }

        public void Read(GamePacketReader reader)
        {
            (DataSet, _) = AreaDefinitionHeader.Read(reader);
            To = AreaDefinitionRecord.Read(reader);
            From = AreaDefinitionRecord.Read(reader);
            ShapeGuid = reader.ReadULong();
        }
    }
}
