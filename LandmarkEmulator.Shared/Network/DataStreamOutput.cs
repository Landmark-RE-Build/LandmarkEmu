using LandmarkEmulator.Shared.Network.Cryptography;
using NLog;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace LandmarkEmulator.Shared.Network
{
    public class DataStreamOutput : DataStreamBase
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Max reliable sequences allowed between pending and the send cursor
        /// before the send path should pause. UdpParams profile 4 <c>maxOutstandingPackets</c>.
        /// </summary>
        public const int MaxUnackedReliablePackets = 400;

        // UdpParams profile 4 ch0 — see AgentShare/GhidraAnswers1554.md.
        public const int ResendDelayAdjustMs = 300;
        public const int ResendDelayPercent = 125;
        public const int ResendDelayCapMs = 8000;
        public const int AckPingInitMs = 800;
        public const int OldestUnackedTimeoutMs = 120000;

        /// <summary>
        /// Channel <c>ackAveragePing</c>: init 800, EWMA <c>(rtt + 3×old)/4</c> on first-try ack
        /// Not fed by NetStatus.
        /// </summary>
        public int AckPingMs { get; private set; } = AckPingInitMs;

        public int CurrentRtoMs =>
            Math.Min(ResendDelayCapMs, ResendDelayAdjustMs + AckPingMs * ResendDelayPercent / 100);

        public delegate void DataEvent(ushort sequence, DataPacket packet, bool wasOutOfOrder = false);
        /// <summary>
        /// Raised on data processing complete, and packet available.
        /// </summary>
        public event DataEvent OnData;

        /// <summary>
        /// True when any sent reliable sequence is still cached (awaiting ack).
        /// </summary>
        public bool MissingAcks => UnackedCount > 0;

        /// <summary>Highest reliable sequence actually written to UDP (not merely packed).</summary>
        public ushort? HighestSentSequence { get; private set; }

        /// <summary>See <see cref="UnackedCountLocked"/> (read by the out thread for its send window).</summary>
        public int UnackedCount
        {
            get
            {
                lock (sync)
                    return UnackedCountLocked;
            }
        }

        /// <summary>
        /// Reliable sequences still cached between the pending cursor and
        /// <see cref="HighestSentSequence"/> (selective acks may leave holes).
        /// Desync reports over-window so the sender pauses.
        /// </summary>
        private int UnackedCountLocked
        {
            get
            {
                if (!HighestSentSequence.HasValue)
                    return 0;

                ushort pending = PendingSequence;
                ushort high = HighestSentSequence.Value;

                // Pending past the send high-water => fully acked through everything sent.
                if (pending == (ushort)(high + 1))
                    return 0;
                int pendingAheadOfHigh = (pending - high + 65536) % 65536;
                if (pendingAheadOfHigh > 0 && pendingAheadOfHigh < 32768)
                    return 0;

                int count = 0;
                ushort idx = pending;
                while (true)
                {
                    if (DataPackets[idx] != null)
                        count++;

                    if (idx == high)
                        break;

                    idx = (ushort)(idx + 1);
                    if (count > MaxUnackedReliablePackets)
                        return MaxUnackedReliablePackets + 1;
                }

                return count;
            }
        }

        public bool CanSendMoreReliable => UnackedCount < MaxUnackedReliablePackets;

        /// <summary>
        /// Oldest not-yet-acked sequence (client <c>reliableOutgoingPendingId</c>).
        /// </summary>
        private ushort PendingSequence => LastAck.HasValue ? (ushort)(LastAck.Value + 1) : (ushort)0;

        /// <summary>
        /// Client <c>reliableOutgoingId</c>: next sequence that would be assigned
        /// (<see cref="NextSequence"/> is last assigned).
        /// </summary>
        private uint OutgoingId => NextSequence.HasValue ? (uint)NextSequence.Value + 1u : 0u;

        /// <summary>
        /// Guards the send cache and cursors. Three threads use them: the tick packs (<see cref="PackData"/>) and runs
        /// the RTO resends, the UDP receive thread takes the client's acks, the out thread marks sends and reads the
        /// window. Unguarded, an ack landing between PackData's <c>NextSequence++</c> and its <c>DataPackets[...] =</c>
        /// saw an empty slot inside the assigned range: AdvancePendingOverHoles moved LastAck past the packet before it
        /// was even cached, so it counted as acked, was never resent when lost, and the client's reliable stream waited
        /// for that sequence until the session died (the "LastAck desync" repair in TryAcceptAckSequence treats a symptom).
        /// </summary>
        private readonly object sync = new();

        private uint _fragmentSize;
        private Arc4Provider arc4Provider;
        private int _ignoredAheadAckLogBudget = 8;

        public DataStreamOutput(GameSession session)
            : base (session)
        {
            arc4Provider = new Arc4Provider(RC4Key);
        }

        /// <summary>Record that a reliable Data/DataFragment with this sequence hit UDP (out thread).</summary>
        public void NotifySent(ushort sequence)
        {
            lock (sync)
                NotifySentLocked(sequence);
        }

        /// <summary>Record that a reliable Data/DataFragment with this sequence hit UDP.</summary>
        private void NotifySentLocked(ushort sequence)
        {
            long now = DateTime.UtcNow.Ticks;
            DataPacket cached = DataPackets[sequence];
            if (cached != null)
            {
                bool firstSend = cached.SendCount == 0;
                cached.MarkSent(now);
                if (firstSend)
                    _session?.ReliabilityStats.RecordOutboundFirstSend();
            }

            if (!HighestSentSequence.HasValue)
            {
                HighestSentSequence = sequence;
                return;
            }

            // Resends must not pull the high-water mark backward (would open the send window).
            int ahead = (sequence - HighestSentSequence.Value + 65536) % 65536;
            if (ahead > 0 && ahead < 32768)
                HighestSentSequence = sequence;
        }

        /// <summary>Encrypts, sequences and caches a packet for sending (tick thread).</summary>
        public void PackData(byte[] data)
        {
            lock (sync)
                PackDataLocked(data);
        }

        private void PackDataLocked(byte[] data)
        {
            if (UsingEncryption)
                arc4Provider.Encrypt(data);

            if (data[0] == 0)
            {
                List<byte> tmp = new List<byte> { 0 };
                tmp.AddRange(data);
                data = tmp.ToArray();
            }

            if (data.Length <= _fragmentSize)
            {
                NextSequence = NextSequence.HasValue ? (ushort)(NextSequence.Value + 1) : (ushort)0;

                var dataPacket = new DataPacket(data, false);
                DataPackets[(int)NextSequence] = dataPacket;
                OnData((ushort)NextSequence, dataPacket);
                return;
            }

            // Fragmented form: big-endian u32 total length, then the payload. Slice in place —
            // re-materialising the whole buffer per fragment cost seconds on ~MB CellsUpdate
            // megapackets and blew the client 6s SCENE request budget.
            byte[] framed = new byte[sizeof(uint) + data.Length];
            BinaryPrimitives.WriteUInt32BigEndian(framed.AsSpan(0, sizeof(uint)), (uint)data.Length);
            data.CopyTo(framed, sizeof(uint));

            int fragmentSize = (int)_fragmentSize;
            for (var i = 0; i < framed.Length; i += fragmentSize)
            {
                NextSequence = NextSequence.HasValue ? (ushort)(NextSequence.Value + 1) : (ushort)0;

                int nextSize = Math.Min(fragmentSize, framed.Length - i);
                var dataPacket = new DataPacket(framed.AsSpan(i, nextSize).ToArray(), true);
                DataPackets[(int)NextSequence] = dataPacket;
                OnData((ushort)NextSequence, dataPacket);
            }
        }

        public void SetFragmentSize(uint fragmentSize)
        {
            _fragmentSize = fragmentSize;
        }

        /// <summary>Single-sequence Acknowledge (<c>0x11</c>) from the client (receive thread); see <see cref="HandleAckLocked"/>.</summary>
        public void HandleAck(ushort sequence)
        {
            lock (sync)
                HandleAckLocked(sequence);
        }

        /// <summary>
        /// Single-sequence Acknowledge (<c>0x11</c>). Clears that slot only; advances
        /// <see cref="LastAck"/> over leading holes (as the client does).
        /// Selective ack of a later seq accelerates resend of earlier slots whose
        /// <see cref="DataPacket.LastSendTicks"/> is older than the acked packet's first send.
        /// </summary>
        private void HandleAckLocked(ushort sequence)
        {
            if (!TryAcceptAckSequence(sequence, out ushort seq))
                return;

            ushort pendingBefore = PendingSequence;
            long marker = DataPackets[seq]?.FirstSendTicks ?? 0;
            ReleaseSlot(seq);

            // Accelerated hole-fill (client resentPacketsAccelerated): resend earlier
            // unacked slots with lastSend &lt; acked packet's first-send marker.
            int fromPending = (seq - pendingBefore + 65536) % 65536;
            if (marker > 0 && fromPending > 0 && fromPending < 32768)
            {
                for (int i = 0; i < fromPending; i++)
                {
                    ushort idx = (ushort)(pendingBefore + i);
                    DataPacket pkt = DataPackets[idx];
                    if (pkt != null && pkt.LastSendTicks > 0 && pkt.LastSendTicks < marker && QueueResend(idx, pkt))
                        _session?.ReliabilityStats.RecordOutboundAcceleratedResend();
                }
            }

            AdvancePendingOverHoles();
        }

        /// <summary>AcknowledgeAll (<c>0x15</c>) from the client (receive thread); see <see cref="HandleAckAllLocked"/>.</summary>
        public void HandleAckAll(ushort sequence)
        {
            lock (sync)
                HandleAckAllLocked(sequence);
        }

        /// <summary>
        /// AcknowledgeAll (<c>0x15</c>). Clears every cached sequence through
        /// <paramref name="sequence"/> inclusive (as the client does).
        /// </summary>
        private void HandleAckAllLocked(ushort sequence)
        {
            if (!TryAcceptAckSequence(sequence, out ushort seq))
                return;

            ushort pending = PendingSequence;
            int fromPendingToSeq = (seq - pending + 65536) % 65536;
            // seq behind pending (stale AckAll) -- should already be rejected by unwrap.
            if (fromPendingToSeq > 32768)
                return;

            for (int i = 0; i <= fromPendingToSeq; i++)
            {
                ushort idx = (ushort)(pending + i);
                ReleaseSlot(idx);
            }

            LastAck = seq;
            AdvancePendingOverHoles();
        }

        /// <summary>
        /// Clear one cached slot; on first-try ack update <see cref="AckPingMs"/> EWMA.
        /// </summary>
        private void ReleaseSlot(ushort seq)
        {
            DataPacket pkt = DataPackets[seq];
            if (pkt != null && pkt.SendCount == 1 && pkt.FirstSendTicks > 0)
            {
                int rttMs = (int)((DateTime.UtcNow.Ticks - pkt.FirstSendTicks) / TimeSpan.TicksPerMillisecond);
                if (rttMs < 0)
                    rttMs = 0;
                AckPingMs = (rttMs + 3 * AckPingMs) / 4;
            }

            DataPackets[seq] = null;
        }

        /// <summary>
        /// Client-faithful unwrap + gate: reconstruct against outgoingId, pull back one
        /// 64K block if ahead, reject stale (<c>seq &lt; pendingId</c>), accept only
        /// <c>pendingId &lt;= seq &lt; outgoingId</c>.
        /// </summary>
        private bool TryAcceptAckSequence(ushort sequence, out ushort seq)
        {
            seq = sequence;

            if (!NextSequence.HasValue)
                return false;

            // Repair cursor if a prior bug left LastAck ahead of what we have sent.
            if (LastAck.HasValue)
            {
                int unackedPacked = (NextSequence.Value - LastAck.Value + 65536) % 65536;
                if (unackedPacked > 32768)
                {
                    log.Warn($"SOE LastAck desync: lastAck={LastAck} next={NextSequence}; realigning LastAck behind send cursor");
                    ushort anchor = HighestSentSequence ?? NextSequence.Value;
                    int back = Math.Min(MaxUnackedReliablePackets, (int)anchor + 1);
                    LastAck = back > 0 ? (ushort)(anchor - back) : null;
                }
            }

            uint outgoingId = OutgoingId;
            uint pendingId = LastAck.HasValue ? (uint)LastAck.Value + 1u : 0u;

            uint full = sequence | (outgoingId & ~0xFFFFu);
            if (full > outgoingId)
                full -= 0x10000u;

            if (full < pendingId)
                return false;

            if (full >= outgoingId)
            {
                if (_ignoredAheadAckLogBudget > 0)
                {
                    _ignoredAheadAckLogBudget--;
                    log.Warn($"Ignoring Ack/AckAll {sequence} (unwrapped={full}) not in [{pendingId},{outgoingId}) (remaining logs={_ignoredAheadAckLogBudget})");
                }
                return false;
            }

            seq = (ushort)(full & 0xFFFF);
            return true;
        }

        /// <summary>
        /// Advance <see cref="LastAck"/> while the pending slot is empty and still at/under last assigned.
        /// </summary>
        private void AdvancePendingOverHoles()
        {
            if (!NextSequence.HasValue)
                return;

            ushort lastAssigned = NextSequence.Value;
            while (true)
            {
                ushort pending = PendingSequence;

                // Caught up through every assigned sequence.
                if (pending == (ushort)(lastAssigned + 1))
                    break;

                int pendingAhead = (pending - lastAssigned + 65536) % 65536;
                if (pendingAhead > 0 && pendingAhead < 32768)
                    break;

                if (DataPackets[pending] != null)
                    break;

                LastAck = pending;
            }
        }

        /// <summary>See <see cref="IsAckedThroughLocked"/>.</summary>
        public bool IsAckedThrough(ushort sequence)
        {
            lock (sync)
                return IsAckedThroughLocked(sequence);
        }

        /// <summary>
        /// Queues a resend of a cached sequence unless one is already waiting to go out (false then). Its age (and so the
        /// RTO check) only moves when it actually hits UDP (<see cref="NotifySentLocked"/>): before this guard every tick
        /// (the world tick runs about every millisecond) queued every overdue packet again while the earlier copies still
        /// waited, so a client that stopped acking for a moment (a loading hitch) got a growing storm of duplicates, and a
        /// session whose sending was paused (CanProcessPackets off) grew its resend queue without bound.
        /// </summary>
        private bool QueueResend(ushort sequence, DataPacket packet)
        {
            if (packet.ResendQueued)
                return false;
            packet.ResendQueued = true;
            OnData(sequence, packet, wasOutOfOrder: true);
            return true;
        }

        /// <summary>
        /// True when contiguous acks cover <paramref name="sequence"/> and
        /// <see cref="LastAck"/> is not ahead of <see cref="NextSequence"/>.
        /// </summary>
        private bool IsAckedThroughLocked(ushort sequence)
        {
            if (!LastAck.HasValue || !NextSequence.HasValue)
                return false;

            // LastAck must lie on the arc [sequence, NextSequence] (inclusive), wrap-aware.
            int fromSeqToAck = (LastAck.Value - sequence + 65536) % 65536;
            int fromAckToNext = (NextSequence.Value - LastAck.Value + 65536) % 65536;
            return fromSeqToAck < 32768 && fromAckToNext < 32768;
        }

        /// <summary>RTO tick (tick thread); see <see cref="ProcessRtoResendsLocked"/>.</summary>
        public bool ProcessRtoResends()
        {
            lock (sync)
                return ProcessRtoResendsLocked();
        }

        /// <summary>
        /// RTO tick: retransmit occupied slots whose last send is older than the current RTO.
        /// Returns true if the oldest unacked age exceeds <see cref="OldestUnackedTimeoutMs"/>.
        /// </summary>
        private bool ProcessRtoResendsLocked()
        {
            if (!HighestSentSequence.HasValue)
                return false;

            long now = DateTime.UtcNow.Ticks;
            long rtoTicks = TimeSpan.FromMilliseconds(CurrentRtoMs).Ticks;
            long oldestTimeoutTicks = TimeSpan.FromMilliseconds(OldestUnackedTimeoutMs).Ticks;

            ushort high = HighestSentSequence.Value;
            ushort idx = PendingSequence;
            if (idx == (ushort)(high + 1))
                return false;

            long oldestAge = 0;
            while (true)
            {
                DataPacket pkt = DataPackets[idx];
                if (pkt != null && pkt.LastSendTicks > 0)
                {
                    long age = now - pkt.LastSendTicks;
                    if (age > oldestAge)
                        oldestAge = age;
                    // Exponential backoff per packet: each resend doubles the wait (capped). Without it a client that
                    // acks slowly (long frames while it loads terrain) has its whole window of up to
                    // MaxUnackedReliablePackets resent every RTO, several times the original traffic, which slows it
                    // down further.
                    int resends = Math.Min(pkt.SendCount - 1, 4);
                    long backoffTicks = Math.Min(rtoTicks << resends, TimeSpan.FromMilliseconds(ResendDelayCapMs).Ticks);
                    if (age >= backoffTicks && QueueResend(idx, pkt))
                        _session?.ReliabilityStats.RecordOutboundRtoResend();
                }

                if (idx == high)
                    break;
                idx = (ushort)(idx + 1);
            }

            return oldestAge >= oldestTimeoutTicks;
        }

        /// <summary>See <see cref="ResendAllUnackedLocked"/>.</summary>
        public void ResendAllUnacked()
        {
            lock (sync)
                ResendAllUnackedLocked();
        }

        /// <summary>
        /// Force-resend all still-cached packets from pending through the send high-water
        /// (terrain soft-deadline kick). Does not pretend Ping triggered the resend.
        /// </summary>
        private void ResendAllUnackedLocked()
        {
            if (!HighestSentSequence.HasValue)
                return;

            ushort high = HighestSentSequence.Value;
            ushort idx = PendingSequence;
            if (idx == (ushort)(high + 1))
                return;

            while (true)
            {
                if (DataPackets[idx] is { } pkt && QueueResend(idx, pkt))
                    _session?.ReliabilityStats.RecordOutboundForcedResend();

                if (idx == high)
                    break;
                idx = (ushort)(idx + 1);
            }
        }
    }
}
