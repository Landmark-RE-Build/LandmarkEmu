using LandmarkEmulator.Shared.Network.Cryptography;

namespace LandmarkEmulator.Shared.Network.Message.Model
{
    [ProtocolMessage(ProtocolMessageOpcode.DataFragment, useEncryption: true)]
    public class DataFragment : IProtocol
    {
        public ushort Sequence { get; set; }
        public ushort FragmentEnd { get; set; }
        public byte[] Data { get; set; }
        public PacketOptions PacketOptions { get; private set; }

        public void Read(ProtocolPacketReader reader, PacketOptions options)
        {
            if (options.Compression && !options.IsSubpacket)
            {
                byte[] raw = reader.GetRemainingData();
                reader.ReadBytes((uint)raw.Length);
                byte[] unwrapped = SoeCompression.UnwrapBody(raw, compressionEnabled: true);
                reader = new ProtocolPacketReader(unwrapped);
            }

            Sequence = reader.ReadUShort();
            Data     = reader.ReadBytes((uint)reader.BytesRemaining);
            PacketOptions = options;
        }

        public void Write(ProtocolPacketWriter writer, PacketOptions options)
        {
            if (options.Compression && !options.IsSubpacket)
            {
                var body = new byte[2 + Data.Length];
                body[0] = (byte)(Sequence >> 8);
                body[1] = (byte)Sequence;
                System.Buffer.BlockCopy(Data, 0, body, 2, Data.Length);
                writer.WriteBytes(SoeCompression.WrapBody(body));
                return;
            }

            writer.Write(Sequence);
            writer.WriteBytes(Data);
        }
    }
}
