using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using LandmarkEmulator.Shared.Network.Cryptography;
using LandmarkEmulator.Shared.Network.Message.Model;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.Parser
{
    public class DataStreamParsing : DataStreamBase
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public delegate void DataEvent(byte[] message);
        /// <summary>
        /// Raised on data processing complete, and packet available.
        /// </summary>
        public event DataEvent OnData;

        public delegate void OutOfOrderEvent(ushort sequence);
        /// <summary>
        /// Raised on data processing complete, and packet available.
        /// </summary>
        public event OutOfOrderEvent OnOutOfOrder;

        private int _lastProcessedFragment = -1;
        private Arc4Provider arc4Provider;

        private string loggerName = "";

        public DataStreamParsing(GameSession session, string name = "")
            : base (session)
        {
            arc4Provider = new Arc4Provider(RC4Key);
            loggerName = name+": ";
        }

        public void ProcessDataFragment(DataWhole dataWhole)
        {
            ReadyDataForProcessing(dataWhole.Sequence, dataWhole.Data, false);
        }

        public void ProcessDataFragment(DataFragment dataFragment)
        {
            ReadyDataForProcessing(dataFragment.Sequence, dataFragment.Data, true);
        }

        private void ReadyDataForProcessing(ushort sequence, byte[] data, bool isFragment)
        {
            if (NextSequence == null)
                NextSequence = sequence;

            ushort expected = NextSequence.Value;

            // Duplicate / old: AckAll(expected - 1). Parser does not need to emit protocol.
            int behind = (expected - sequence + 65536) % 65536;
            if (behind > 0 && behind < 32768)
                return;

            if (sequence != expected && OnOutOfOrder != null)
            {
                // Ahead of expected — stash like the client; log for capture diagnostics.
                log.Warn($"{loggerName}Sequence out of order, expected {NextSequence} but received {sequence}");
                OnOutOfOrder(sequence);
            }

            DataPackets[sequence] = new DataPacket(data, isFragment);

            if (sequence == expected)
            {
                ushort ack = sequence;
                for (int i = 1; i <= 0x10000; i++)
                {
                    ushort j = (ushort)((expected + i) & 0xFFFF);
                    if (DataPackets[j] != null)
                        ack = j;
                    else
                        break;
                }

                LastAck = ack;
                NextSequence = (ushort)((ack + 1) & 0xFFFF);

                // One ParseDataFragments emits a single whole/chain. If later sequences were
                // already queued (out-of-order fill), drain them or they stay stranded forever.
                int guard = 0;
                while (guard++ < 0x10000)
                {
                    int before = _lastProcessedFragment;
                    ParseDataFragments();
                    if (_lastProcessedFragment == before)
                        break;
                }
            }
        }

        private void ParseDataFragments()
        {
            var nextFragment = (_lastProcessedFragment + 1) & 0xFFFF;
            List<byte[]> finishedData = new();

            if (DataPackets[nextFragment] == null)
                return;

            var dataPacket = DataPackets[nextFragment];

            if (!dataPacket.IsFragment)
            {
                // Must clear — otherwise after ~64k wholes the ring is full and the ack
                // scan above spins forever (this is what hung enrich on large sessions).
                DataPackets[nextFragment] = null;
                _lastProcessedFragment = nextFragment;
                finishedData = ParseChannelData(dataPacket.Data.ToList());
                DecryptData(finishedData);
                return;
            }

            try
            {
                bool dataReady = false;
                var reader = new ProtocolPacketReader(dataPacket.Data);
                uint totalSize = reader.ReadUInt();
                int dataSize = dataPacket.Data.Length - 4;

                List<byte> data = new();
                for (int i = 4; i < dataPacket.Data.Length; i++)
                    data.Add(dataPacket.Data[i]);

                List<ushort> fragmentIndices = new();
                fragmentIndices.Add((ushort)nextFragment);
                for (int i = 1; i <= 0xFFFF; i++)
                {
                    ushort j = (ushort)((nextFragment + i) & 0xFFFF);
                    var fragment = DataPackets[j];
                    if (fragment != null)
                    {
                        bool dataSizeWillExceed = false;
                        // For the purpose of the Parser DataStream, we'll ignore CRC fixes or checks. Just shrink the remaining data to fit into totalSize
                        // When we're able to load into game, this may need fixes but until then, no need
                        if ((dataSize + fragment.Data.Length) > totalSize)
                            dataSizeWillExceed = true;

                        fragmentIndices.Add(j);
                        int sizeToBuild = (int)(dataSizeWillExceed ? totalSize - dataSize : fragment.Data.Length);
                        for (int m = 0; m < sizeToBuild; m++)
                            data.Add(fragment.Data[m]);
                        dataSize += sizeToBuild;

                        if (dataSize > totalSize)
                            throw new InvalidPacketValueException("DataSize exceeds TotalSize. We've got a problem!");

                        if (dataSize == totalSize)
                        {
                            for (int k = 0; k < fragmentIndices.Count; k++)
                                DataPackets[fragmentIndices[k]] = null;
                            _lastProcessedFragment = j;
                            dataReady = true;
                            finishedData = ParseChannelData(data);
                            break;
                        }
                    }
                    else
                        break;
                }

                if (dataReady)
                    DecryptData(finishedData);
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
        }

        private List<byte[]> ParseChannelData(List<byte> data)
        {
            if (data.Count >= 2 && data[0] == 0x00 && data[1] == 0x19)
            {
                // Match DataStreamInput: only split when lengths exactly consume the body.
                // Encrypted single blobs can accidentally start with 0x0019; a false split desyncs ARC4.
                var reader = new ProtocolPacketReader(data.ToArray().AsSpan().Slice(2).ToArray());
                var pieces = new List<byte[]>();
                bool rejected = false;
                while (reader.BytesRemaining != 0)
                {
                    uint nextLength = ReadLength(reader);
                    if (nextLength == 0 || nextLength > reader.BytesRemaining)
                    {
                        rejected = true;
                        break;
                    }

                    pieces.Add(reader.ReadBytes(nextLength));
                }

                if (!rejected && pieces.Count > 0)
                    return pieces;

                return new List<byte[]> { data.ToArray() };
            }

            return new List<byte[]> { data.ToArray() };
        }

        private uint ReadLength(ProtocolPacketReader reader)
        {
            var data = reader.GetRemainingData();
            uint dataLength = reader.ReadByte();
            if (dataLength == 0xFF)
            {
                if (data[0 + 1] == 0xFF && data[0 + 2] == 0xFF)
                {
                    // Move the current position 2 bytes forward,
                    // because length is placed 3 bytes (we read 1 byte at the beginning of method) ahead of beginning
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

        private void DecryptData(List<byte[]> data)
        {
            for (int i = 0; i < data.Count; i++)
            {
                byte[] piece = data[i];

                var reader = new ProtocolPacketReader(piece);
                byte[] parsedData = piece;
                if (UsingEncryption) // Do we turn off encryption, ever?
                {
                    // sometimes there's an extra 0x00 byte in the beginning that trips up the RC4
                    // (DataStreamOutput.PackData prepends 0x00 when cipher[0]==0 → wire 00 00 …).
                    if (piece.Length > 1 && reader.ReadUShort() == 0)
                    {
                        parsedData = new Span<byte>(piece, 1, piece.Length - 1).ToArray();
                        arc4Provider.Decrypt(parsedData);
                    }
                    else
                        arc4Provider.Decrypt(parsedData);
                }

                OnData(parsedData); // Emit the parsed data packet to subscribers
            }
        }

        public void ResetEncryption()
        {
            arc4Provider = new Arc4Provider(RC4Key);
        }

        public void SetEncryptionKey(string key)
        {
            arc4Provider = new Arc4Provider(Convert.FromBase64String(key));
        }

        /// <summary>
        /// Clears sequence/fragment state and restores the default ARC4 key so a new capture can be parsed.
        /// </summary>
        public void Reset()
        {
            LastAck = null;
            NextSequence = null;
            _lastProcessedFragment = -1;
            Array.Clear(DataPackets, 0, DataPackets.Length);
            arc4Provider = new Arc4Provider(RC4Key);
        }
    }
}

