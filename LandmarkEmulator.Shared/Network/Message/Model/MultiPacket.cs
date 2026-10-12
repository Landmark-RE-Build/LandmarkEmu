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

        private uint ReadLength(ProtocolPacketReader reader)
        {
            var data = reader.GetRemainingData();
            uint dataLength = reader.ReadByte();
            if (dataLength == 0xFF)
            {
                if (data[0 + 1] == 0xFF && data[0 + 2] == 0xFF)
                {
                    reader.ReadUShort();
                    dataLength = reader.ReadUInt();
                }
                else
                {
                    dataLength = reader.ReadUShort();
                }
            }

            return dataLength;
        }

        public void Write(ProtocolPacketWriter writer, PacketOptions options)
        {
            throw new System.NotImplementedException();
        }
    }
}
