using LandmarkEmulator.Shared.Network.Cryptography;
using LandmarkEmulator.Shared.Network.Message.Model;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.Shared.Network
{
    public class DataStreamInput : DataStreamBase
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Drop inbound sequences more than this many ahead of expected
        /// (UdpParams profile 4 <c>maxInstandingPackets</c>).
        /// </summary>
        public const int MaxInstandingPackets = 400;

        public delegate void DataEvent(ushort sequence, byte[] message);
        /// <summary>
        /// Raised on data processing complete, and packet available.
        /// </summary>
        public event DataEvent OnData;

        private int _lastProcessedFragment = -1;
        private Arc4Provider arc4Provider;

        private readonly bool _subPacketAcks;

        public DataStreamInput(GameSession session, bool subPacketAcks = false)
            : base (session)
        {
            arc4Provider = new Arc4Provider(RC4Key);
            // Gateway passes subPacketAcks=true: IsSubpacket omits compression flag on nested acks.
            _subPacketAcks = subPacketAcks;
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
            // The client's reliable stream starts at sequence 0, as ours does (DataStreamOutput: the first packed packet is
            // 0) and as the parse cursor below assumes (_lastProcessedFragment = -1 → slot 0 first). This used to start
            // at whatever sequence arrived first: when packet 0 was lost or overtaken, expected became 1+, the parser
            // still waited for slot 0, and the resent 0 was then "behind" expected and only answered with an AckAll —
            // the client counted it delivered, nothing was ever parsed, and the session hung until it timed out.
            if (NextSequence == null)
                NextSequence = (ushort)((_lastProcessedFragment + 1) & 0xFFFF);

            ushort expected = NextSequence.Value;
            // Match session compression framing (Auth flag byte; Gateway none).
            // Prefer the inbound packet's options when the session set Compression on OnData.
            // An Ack carries the SOE compression flag byte exactly when the session negotiated compression.
            bool compressed = _session?.UsesSoeCompression ?? !_subPacketAcks;
            var ackOptions = new Message.PacketOptions
            {
                IsSubpacket = !compressed,
                Compression = compressed
            };

            // Too far ahead of expected: drop (no stash, no Ack) — client outOfRangePacketsReceived.
            int ahead = (sequence - expected + 65536) % 65536;
            if (ahead > MaxInstandingPackets && ahead < 32768)
            {
                _session?.ReliabilityStats.RecordInboundDroppedOutOfRange();
                log.Trace($"Dropping inbound seq {sequence}: {ahead} ahead of expected {expected} (maxInstanding={MaxInstandingPackets})");
                return;
            }

            // Duplicate / old packet: client emits AckAll(expected - 1).
            int behind = (expected - sequence + 65536) % 65536;
            if (behind > 0 && behind < 32768)
            {
                _session?.ReliabilityStats.RecordInboundDuplicate();
                _session?.EnqueueProtocolMessage(new AckAll
                {
                    Sequence = (ushort)((expected - 1) & 0xFFFF)
                }, ackOptions);
                return;
            }

            // In-order or ahead: stash and emit single Ack for this sequence (as the client does).
            if (ahead > 0 && ahead < 32768)
                _session?.ReliabilityStats.RecordInboundGapHeld();
            else
                _session?.ReliabilityStats.RecordInboundAccepted();

            DataPackets[sequence] = new DataPacket(data, isFragment);
            _session?.EnqueueProtocolMessage(new Ack
            {
                Sequence = sequence
            }, ackOptions);

            // Only advance the expected cursor over contiguous received slots.
            if (sequence == expected)
            {
                ushort ack = sequence;
                for (ushort i = 1; i <= ushort.MaxValue; i++)
                {
                    ushort j = (ushort)((expected + i) & 0xFFFF);
                    if (DataPackets[j] != null)
                        ack = j;
                    else
                        break;
                }

                LastAck = ack;
                NextSequence = (ushort)((ack + 1) & 0xFFFF);
                // Everything now contiguous is parsed, not just the first packet: when a gap fills, the packets held
                // behind it are complete too. Parsing one per arrival (as before) left the rest waiting for later
                // arrivals, so after every loss or reordering each client packet was handled one (then two, three...)
                // packets late — and the last packet of a burst (a ClientIsReady, a logout) not at all until the client
                // happened to send something else.
                while (ParseDataFragments())
                {
                }
            }
        }

        /// <summary>
        /// Parses the next whole packet or complete fragment group after the last one parsed; false when it isn't all
        /// here yet (or nothing is waiting).
        /// </summary>
        private bool ParseDataFragments()
        {
            var nextFragment = (_lastProcessedFragment + 1) & 0xFFFF;
            List<byte[]> finishedData = new();

            if (DataPackets[nextFragment] == null)
                return false;

            var dataPacket = DataPackets[nextFragment];

            if (!dataPacket.IsFragment)
            {
                DataPackets[nextFragment] = null;
                _lastProcessedFragment = nextFragment;
                finishedData = ParseChannelData(dataPacket.Data.ToList());
                DecryptData(finishedData, (ushort)nextFragment);
                return true;
            }
            
            bool dataReady = false;
            var reader = new ProtocolPacketReader(dataPacket.Data);
            uint totalSize = reader.ReadUInt();
            int dataSize = dataPacket.Data.Length - 4;

            List<byte> data = new();
            for (int i = 4; i < dataPacket.Data.Length; i++)
                data.Add(dataPacket.Data[i]);

            List<ushort> fragmentIndices = new();
            fragmentIndices.Add((ushort)nextFragment);
            for (int i = 1; i <= ushort.MaxValue; i++)
            {
                ushort j = (ushort)((nextFragment + i) & 0xFFFF);
                var fragment = DataPackets[j];
                if (fragment != null)
                {
                    fragmentIndices.Add(j);
                    for (int m = 0; m < fragment.Data.Length; m++)
                        data.Add(fragment.Data[m]);
                    dataSize += fragment.Data.Length;

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
                DecryptData(finishedData, fragmentIndices[0]);
            return dataReady;
        }

        /// <summary>
        /// Split a 0x00 0x19 multiplex SOE payload into length-prefixed pieces.
        /// Returns false when lengths do not exactly consume the body (false-positive
        /// 0x0019 on encrypted single blobs must fall back to one-piece decrypt).
        /// </summary>
        private static bool TrySplitMultiplex(List<byte> data, out List<byte[]> pieces)
        {
            pieces = null;
            if (data.Count < 2 || data[0] != 0x00 || data[1] != 0x19)
                return false;

            var reader = new ProtocolPacketReader(data.ToArray().AsSpan().Slice(2).ToArray());
            var result = new List<byte[]>();
            while (reader.BytesRemaining > 0)
            {
                uint nextLength = ReadLengthStatic(reader);
                if (nextLength == 0 || nextLength > reader.BytesRemaining)
                    return false;

                result.Add(reader.ReadBytes(nextLength));
            }

            if (result.Count == 0)
                return false;

            pieces = result;
            return true;
        }

        private List<byte[]> ParseChannelData(List<byte> data)
        {
            if (data.Count >= 2 && data[0] == 0x00 && data[1] == 0x19)
            {
                if (TrySplitMultiplex(data, out var pieces))
                    return pieces;
            }

            return new List<byte[]> { data.ToArray() };
        }

        /// <summary>The same variable length as MultiPacket sub-packets (see <see cref="Message.Model.MultiPacket"/>).</summary>
        private static uint ReadLengthStatic(ProtocolPacketReader reader) => Message.Model.MultiPacket.ReadLength(reader);

        private uint ReadLength(ProtocolPacketReader reader) => ReadLengthStatic(reader);

        private void DecryptData(List<byte[]> data, ushort sequence)
        {
            for (int i = 0; i < data.Count; i++)
            {
                byte[] piece = data[i];
                if (piece.Length == 0)
                    continue;

                var reader = new ProtocolPacketReader(piece);
                byte[] parsedData = piece;
                if (UsingEncryption)
                {
                    // sometimes there's an extra 0x00 byte in the beginning that trips up the RC4
                    if (piece.Length > 1 && reader.ReadUShort() == 0)
                    {
                        parsedData = new Span<byte>(piece, 1, piece.Length - 1).ToArray();
                        arc4Provider.Decrypt(parsedData);
                    }
                    else
                        arc4Provider.Decrypt(parsedData);
                }

                OnData(sequence, parsedData);
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
    }
}
