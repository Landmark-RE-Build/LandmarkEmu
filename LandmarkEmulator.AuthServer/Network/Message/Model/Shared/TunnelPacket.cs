using LandmarkEmulator.AuthServer.Network.Message.Static;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System;
using System.Collections.Generic;

namespace LandmarkEmulator.AuthServer.Network.Message.Model.Shared
{
    /// <summary>
    /// The login tunnel (Login::ExternalPackets::PacketTunnelAppPacketServerToClient 0x11 / ClientToServer 0x10; 0.695
    /// XML reader FUN_143a51d10 names the fields [P]): u64 ServerId, then "Payload", a byte array (u32 length + bytes).
    /// The payload's first byte is the tunnel data type and the rest its body (<see cref="ITunnelData"/>, picked by
    /// <see cref="Type"/> through <see cref="TunnelDataManager"/>, as the login code dispatches it).
    /// </summary>
    /// <remarks>
    /// Read takes exactly the payload's length, so an unknown type, or a body its model does not read to the end, is
    /// kept whole in <see cref="Raw"/> and written back unchanged; Write sizes the payload from the bytes it writes, not
    /// from a model's own size count.
    /// </remarks>
    public class TunnelPacket : IReadable, IWritable
    {
        public ulong ServerId { get; set; }
        public TunnelDataType Type { get; set; }
        /// <summary>The body's model; null when <see cref="Type"/> has none (then <see cref="Raw"/> is sent).</summary>
        public ITunnelData Data { get; set; }
        /// <summary>The body as bytes when there is no model for it (unknown type, or a body that did not parse).</summary>
        public byte[] Raw { get; set; } = Array.Empty<byte>();

        public void Read(GamePacketReader reader)
        {
            ServerId = reader.ReadULong();
            uint size = reader.ReadUInt();
            if (size == 0 || size > reader.BytesRemaining)
                throw new System.IO.InvalidDataException($"Tunnel payload of {size} bytes ({reader.BytesRemaining} left) refused.");
            Type = (TunnelDataType)reader.ReadByte();
            byte[] body = reader.ReadBytes(size - 1);

            Data = null;
            Raw = body;
            ITunnelData data = TunnelDataManager.Instance.NewEntityCommand(Type);
            if (data == null)
                return;
            try
            {
                var bodyReader = new GamePacketReader(body);
                data.Read(bodyReader);
                if (bodyReader.BytesRemaining == 0)
                {
                    Data = data;
                    Raw = Array.Empty<byte>();
                }
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // Kept as raw bytes: the caller sees Data null.
            }
        }

        public void Write(GamePacketWriter writer)
        {
            var body = new List<byte>();
            if (Data != null)
                Data.Write(new GamePacketWriter(body));
            else
                body.AddRange(Raw ?? Array.Empty<byte>());

            writer.Write(ServerId);
            writer.Write((uint)body.Count + 1u);
            writer.Write((byte)Type);
            writer.WriteBytes(body.ToArray());
        }
    }
}
