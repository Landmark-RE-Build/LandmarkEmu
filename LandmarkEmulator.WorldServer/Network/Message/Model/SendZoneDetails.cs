using System;
﻿using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model.Shared;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// SendZoneDetails (S→C): the zone's geometry name (e.g. OpenBetaGeometry_406), type, the continent and its bitmaps,
    /// and sky data. Sent at zone entry; most fields keep the values of a live capture.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.SendZoneDetails)]
    public class SendZoneDetails : IReadable, IWritable
    {
        public string Unknown0 { get; set; }
        public uint Unknown1 { get; set; }
        public bool Unknown2 { get; set; }
        public string Unknown3 { get; set; }
        public uint Unknown4 { get; set; }
        public uint Unknown5 { get; set; }
        public ulong Unknown6 { get; set; }
        public uint Unknown7 { get; set; }
        public uint Unknown8 { get; set; }
        public uint Unknown9 { get; set; }
        public Sky SkyData { get; set; } = new();

        public void Read(GamePacketReader reader)
        {
            Unknown0 = reader.ReadString();
            Unknown1 = reader.ReadUInt();
            Unknown2 = reader.ReadBool();
            Unknown3 = reader.ReadString();
            Unknown4 = reader.ReadUInt();
            Unknown5 = reader.ReadUInt();
            Unknown6 = reader.ReadULong();
            Unknown7 = reader.ReadUInt();
            Unknown8 = reader.ReadUInt();
            Unknown9 = reader.ReadUInt();

            SkyData.Read(reader);
            Trailing = reader.ReadBytes(reader.BytesRemaining);
        }

        /// <summary>Bytes after the sky data that the model doesn't name yet (one in the 406 capture); written back as is.</summary>
        public byte[] Trailing { get; set; } = Array.Empty<byte>();

        public void Write(GamePacketWriter writer)
        {
            writer.Write(Unknown0);
            writer.Write(Unknown1);
            writer.Write(Unknown2);
            writer.Write(Unknown3);
            writer.Write(Unknown4);
            writer.Write(Unknown5);
            writer.Write(Unknown6);
            writer.Write(Unknown7);
            writer.Write(Unknown8);
            writer.Write(Unknown9);

            SkyData.Write(writer);
            if (Trailing.Length > 0)
                writer.WriteBytes(Trailing);
        }
    }
}
