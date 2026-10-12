using NLog;
using System;
using System.Threading;

namespace LandmarkEmulator.Shared.Network
{
    /// <summary>
    /// Per-session SOE reliability counters rolled up on a fixed interval (default 60s).
    /// </summary>
    public sealed class SoeReliabilityStats
    {
        public const int DefaultReportIntervalMs = 60_000;

        private readonly int _reportIntervalMs;
        private long _periodStartUtcTicks = DateTime.UtcNow.Ticks;

        private int _outboundFirstSend;
        private int _outboundRtoResend;
        private int _outboundAcceleratedResend;
        private int _outboundForcedResend;
        private int _inboundAccepted;
        private int _inboundGapHeld;
        private int _inboundDuplicate;
        private int _inboundDroppedOutOfRange;
        private int _clientAck;
        private int _clientAckAll;
        private int _sendWindowPause;

        public SoeReliabilityStats(int reportIntervalMs = DefaultReportIntervalMs)
        {
            _reportIntervalMs = reportIntervalMs > 0 ? reportIntervalMs : DefaultReportIntervalMs;
        }

        public void RecordOutboundFirstSend() => Interlocked.Increment(ref _outboundFirstSend);

        public void RecordOutboundRtoResend() => Interlocked.Increment(ref _outboundRtoResend);

        public void RecordOutboundAcceleratedResend() => Interlocked.Increment(ref _outboundAcceleratedResend);

        public void RecordOutboundForcedResend() => Interlocked.Increment(ref _outboundForcedResend);

        public void RecordInboundAccepted() => Interlocked.Increment(ref _inboundAccepted);

        public void RecordInboundGapHeld() => Interlocked.Increment(ref _inboundGapHeld);

        public void RecordInboundDuplicate() => Interlocked.Increment(ref _inboundDuplicate);

        public void RecordInboundDroppedOutOfRange() => Interlocked.Increment(ref _inboundDroppedOutOfRange);

        public void RecordClientAck() => Interlocked.Increment(ref _clientAck);

        public void RecordClientAckAll() => Interlocked.Increment(ref _clientAckAll);

        public void RecordSendWindowPause() => Interlocked.Increment(ref _sendWindowPause);

        /// <summary>
        /// Emit a period summary when the interval elapses. Call from the session tick thread.
        /// </summary>
        public void MaybeReport(GameSession session, ILogger log, bool force = false)
        {
            if (session == null)
                return;

            long now = DateTime.UtcNow.Ticks;
            long elapsedMs = (now - Interlocked.Read(ref _periodStartUtcTicks)) / TimeSpan.TicksPerMillisecond;
            if (!force && elapsedMs < _reportIntervalMs)
                return;

            int firstSend = Interlocked.Exchange(ref _outboundFirstSend, 0);
            int rto = Interlocked.Exchange(ref _outboundRtoResend, 0);
            int accelerated = Interlocked.Exchange(ref _outboundAcceleratedResend, 0);
            int forced = Interlocked.Exchange(ref _outboundForcedResend, 0);
            int inboundOk = Interlocked.Exchange(ref _inboundAccepted, 0);
            int inboundGap = Interlocked.Exchange(ref _inboundGapHeld, 0);
            int inboundDup = Interlocked.Exchange(ref _inboundDuplicate, 0);
            int inboundDrop = Interlocked.Exchange(ref _inboundDroppedOutOfRange, 0);
            int ack = Interlocked.Exchange(ref _clientAck, 0);
            int ackAll = Interlocked.Exchange(ref _clientAckAll, 0);
            int windowPause = Interlocked.Exchange(ref _sendWindowPause, 0);

            Interlocked.Exchange(ref _periodStartUtcTicks, now);

            int periodMs = force ? (int)elapsedMs : _reportIntervalMs;
            int unacked = session.OutputUnackedCount;
            int ackPing = session.OutputAckPingMs;
            int rtoMs = session.OutputCurrentRtoMs;
            bool queued = session.HasQueuedDataPackets;

            log.Info("SOE compression (all sessions so far): {In} bytes in, {Out} bytes out",
                Interlocked.Read(ref Cryptography.SoeCompression.BytesIn), Interlocked.Read(ref Cryptography.SoeCompression.BytesOut));
            log.Info(
                "SOE reliability ({PeriodMs}ms): soeSession={SoeSessionId} backend={BackendSessionId} " +
                "outFirstSend={OutFirstSend} outRtoResend={OutRtoResend} outAccelResend={OutAccelResend} outForcedResend={OutForcedResend} " +
                "inAccepted={InAccepted} inGapHeld={InGapHeld} inDuplicate={InDuplicate} inDropped={InDropped} " +
                "clientAck={ClientAck} clientAckAll={ClientAckAll} sendWindowPause={SendWindowPause} " +
                "unackedNow={UnackedNow} ackPingMs={AckPingMs} rtoMs={RtoMs} queuedData={QueuedData}",
                periodMs,
                session.SoeSessionId,
                session.BackendSessionId,
                firstSend,
                rto,
                accelerated,
                forced,
                inboundOk,
                inboundGap,
                inboundDup,
                inboundDrop,
                ack,
                ackAll,
                windowPause,
                unacked,
                ackPing,
                rtoMs,
                queued);
        }
    }
}
