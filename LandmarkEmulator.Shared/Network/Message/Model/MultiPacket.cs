using LandmarkEmulator.Shared.Network.Cryptography;
using LandmarkEmulator.Shared.Network.Packets;
using NLog;
using System;
using System.Collections.Generic;

namespace LandmarkEmulator.Shared.Network.Message.Model
{
    [ProtocolMessage(ProtocolMessageOpcode.MultiPacket, useEncryption: true)]
    public class MultiPacket : IProtocol
    {
        protected static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public List<ProtocolPacket> Packets { get; } = new();

        public void Read(ProtocolPacketReader reader, PacketOptions options)
        {
            byte[] raw = reader.GetRemainingData();
            if (raw.Length > 0)
                reader.ReadBytes((uint)raw.Length);

            // Method-1 wire: flag + (raw | zlib). Inflate before length-prefixed subpackets.
            // Subpackets never carry their own SOE compression flag.
            byte[] body = options.Compression && !options.IsSubpacket
                ? SoeCompression.UnwrapBody(raw, compressionEnabled: true)
                : raw;

            var inner = new ProtocolPacketReader(body);
            while (inner.BytesRemaining > 2)
            {
                int before = inner.BytesRemaining;
                if (!TryReadPacket(inner, options))
                    break;
                if (inner.BytesRemaining >= before)
                {
                    log.Warn($"MultiPacket made no progress ({inner.BytesRemaining} bytes left); stopping");
                    break;
                }
            }
        }

        private bool TryReadPacket(ProtocolPacketReader reader, PacketOptions options)
        {
            uint nextLength = ReadLength(reader);
            if (nextLength == 0 || nextLength > reader.BytesRemaining)
                return false;

            Packets.Add(new ProtocolPacket(reader.ReadBytes(nextLength),
                new PacketOptions
                {
                    IsSubpacket = true,
                    Compression = false
                }));
            return true;
        }

        /// <summary>
        /// A sub-packet length: one byte below 0xFF; 0xFF + u16 big-endian; 0xFF 0xFF 0xFF + u32 big-endian (client
        /// reader FUN_142ad35e0). The client also sends a 255-byte sub-packet with the single byte 0xFF (seen
        /// 2026-10-13: "FF 00 09 02 3F …" = 255 bytes of Data seq 0x023F). Misreading it as 0xFF + u16 0x0009 cut
        /// the packet apart, the reliable stream never got that sequence and the client timed out 90 s later. The
        /// long forms are only used from 0xFE up (writer FUN_142ad3650), and every SOE packet starts 00 01..00 1D, so
        /// 0xFF followed by a u16 below 0xFE is the single-byte form.
        /// </summary>
        internal static uint ReadLength(ProtocolPacketReader reader)
        {
            var data = reader.GetRemainingData();
            uint dataLength = reader.ReadByte();
            if (dataLength != 0xFF || data.Length < 3)
                return dataLength;
            if (data[1] == 0xFF && data[2] == 0xFF)
            {
                if (data.Length < 7)
                    return 0;
                reader.ReadUShort();
                return reader.ReadUInt();
            }
            uint longForm = (uint)(data[1] << 8 | data[2]);
            if (longForm < 0xFE)
                return 0xFF;
            reader.ReadUShort();
            return longForm;
        }

        public void Write(ProtocolPacketWriter writer, PacketOptions options)
        {
            throw new System.NotImplementedException();
        }
    }
}
