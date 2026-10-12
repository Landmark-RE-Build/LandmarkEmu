using Haukcode.PcapngUtils;
using Haukcode.PcapngUtils.Common;
using System;
using System.Threading;

namespace LandmarkEmulator.Parser
{
    /// <summary>
    /// Streams UDP datagrams from Ethernet / VLAN / raw-IPv4 pcap frames.
    /// Frames are handled as the library emits them — no full-file packet queue.
    /// </summary>
    public static class PcapUdpReader
    {
        public readonly struct UdpDatagram
        {
            public ushort SourcePort { get; init; }
            public ushort DestinationPort { get; init; }
            public byte[] Payload { get; init; }
            public int FrameIndex { get; init; }
            /// <summary>Pcap/pcapng packet timestamp (seconds since epoch or capture clock).</summary>
            public uint Seconds { get; init; }
            /// <summary>Fractional microseconds for <see cref="Seconds"/>.</summary>
            public uint Microseconds { get; init; }
        }

        /// <summary>
        /// Invokes <paramref name="onDatagram"/> for each UDP datagram as it is read.
        /// Return <c>false</c> to stop (cancels further packet delivery when the library honors the token).
        /// </summary>
        public static void ForEach(string filename, Func<UdpDatagram, bool> onDatagram, CancellationToken token = default)
        {
            if (onDatagram == null)
                throw new ArgumentNullException(nameof(onDatagram));

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
            int frameIndex = 0;
            Exception callbackError = null;

            void OnPacket(object context, IPacket packet)
            {
                if (linked.IsCancellationRequested || callbackError != null)
                    return;

                try
                {
                    if (!TryParseUdp(packet.Data, out var datagram))
                        return;

                    datagram = new UdpDatagram
                    {
                        SourcePort = datagram.SourcePort,
                        DestinationPort = datagram.DestinationPort,
                        Payload = datagram.Payload,
                        FrameIndex = frameIndex,
                        Seconds = packet.Seconds,
                        Microseconds = packet.Microseconds
                    };

                    if (!onDatagram(datagram))
                        linked.Cancel();
                }
                catch (Exception ex)
                {
                    callbackError = ex;
                    linked.Cancel();
                }
                finally
                {
                    frameIndex++;
                }
            }

            // Avoid `using` — some Haukcode pcapng readers throw on Dispose after a successful read.
            var reader = IReaderFactory.GetReader(filename);
            Exception readerError = null;
            try
            {
                reader.OnReadPacketEvent += OnPacket;
                try
                {
                    reader.ReadPackets(linked.Token);
                }
                catch (OperationCanceledException)
                {
                    // Expected on early stop or caller cancel.
                }
                catch (Exception ex)
                {
                    // Library sometimes throws a teardown NRE/ANE after streaming large pcapng files.
                    readerError = ex;
                }
            }
            finally
            {
                try { reader.OnReadPacketEvent -= OnPacket; }
                catch { /* ignore */ }
                try { reader.Dispose(); }
                catch { /* ignore dispose quirks on some pcapng variants */ }
            }

            if (callbackError != null)
                throw callbackError;

            // Only surface reader errors when nothing useful was processed (same policy as the old buffered Enumerate).
            if (readerError != null && frameIndex == 0)
                throw readerError;

            if (token.IsCancellationRequested)
                token.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Streams the whole capture (no early exit unless <paramref name="token"/> is cancelled).
        /// </summary>
        public static void ForEach(string filename, Action<UdpDatagram> onDatagram, CancellationToken token = default)
        {
            ForEach(filename, d =>
            {
                onDatagram(d);
                return true;
            }, token);
        }

        public static bool TryParseUdp(byte[] frame, out UdpDatagram datagram)
        {
            datagram = default;
            if (frame == null || frame.Length < 28)
                return false;

            int ipOffset;
            if ((frame[0] >> 4) == 4)
            {
                // Raw IPv4 (no link header).
                ipOffset = 0;
            }
            else if (BitConverter.ToInt32(frame, 0) == 2) {
                // DLT_NULL (Null/Loopback on Windows PCAP)
                ipOffset= 4;
            }
            else if (frame.Length >= 34)
            {
                // Ethernet II (+ optional VLAN).
                ushort etherType = (ushort)((frame[12] << 8) | frame[13]);
                ipOffset = 14;
                if (etherType == 0x8100 && frame.Length >= 38)
                {
                    etherType = (ushort)((frame[16] << 8) | frame[17]);
                    ipOffset = 18;
                }
                if (etherType != 0x0800)
                    return false;
            }
            else
            {
                return false;
            }

            if (frame.Length < ipOffset + 20)
                return false;
            if ((frame[ipOffset] >> 4) != 4)
                return false;

            int ihl = (frame[ipOffset] & 0x0F) * 4;
            if (ihl < 20 || frame.Length < ipOffset + ihl + 8)
                return false;
            if (frame[ipOffset + 9] != 17) // UDP
                return false;

            int udpOffset = ipOffset + ihl;
            ushort srcPort = (ushort)((frame[udpOffset] << 8) | frame[udpOffset + 1]);
            ushort dstPort = (ushort)((frame[udpOffset + 2] << 8) | frame[udpOffset + 3]);
            int payloadOffset = udpOffset + 8;
            if (payloadOffset > frame.Length)
                return false;

            int payloadLen = frame.Length - payloadOffset;
            var payload = new byte[payloadLen];
            Buffer.BlockCopy(frame, payloadOffset, payload, 0, payloadLen);

            datagram = new UdpDatagram
            {
                SourcePort = srcPort,
                DestinationPort = dstPort,
                Payload = payload,
                FrameIndex = 0,
                Seconds = 0,
                Microseconds = 0
            };
            return true;
        }

        /// <summary>
        /// Heuristic: ephemeral client ports are typically above 40000 in Landmark sniffs.
        /// </summary>
        public static bool IsLikelyClient(ushort sourcePort) => sourcePort > 40000;

        public static ushort ServerPort(ushort sourcePort, ushort destPort)
            => IsLikelyClient(sourcePort) ? destPort : sourcePort;
    }
}
