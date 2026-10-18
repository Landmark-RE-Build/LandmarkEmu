using System;
using System.Diagnostics;
using System.Threading;

namespace LandmarkEmulator.Shared.Network
{
    /// <summary>
    /// Inbound silence of one connection: how long since the client last sent anything (any SOE packet counts:
    /// <see cref="GameSession.OnData"/> calls <see cref="OnHeartbeat"/> on the receive thread). A connection that stays
    /// silent for <see cref="TimeoutSeconds"/> has "flatlined": the network manager disconnects it and its owner removes
    /// the character from the world (others get the despawn).
    /// <para>
    /// What a live 0.695 client sends while idle (docs/protocol.md §1.1 "Keep-alives and timeouts"): the SOE keep-alive 0x06 whenever it has
    /// sent nothing for 29 s — UdpLibrary keepAliveDelay, set to 29000 ms for every client manager in the BaseUdpManager
    /// constructor FUN_143a0a980 (manager +0xc8 = params +0x20) and sent by the connection's GiveTime FUN_142b68590 from
    /// the UDP thread (client log: "BaseUdpManager service started (ExternalGatewayApi_3) threading=1"), so it goes out
    /// even while the game's main thread is busy in a loading screen; and while in the world the zone KeepAlive 0x63 every
    /// second (Network.KeepAlive = 1 × 1000 ms, FUN_142cd4e60 run state 0x12) plus Synchronization 0xC3 every 5 s. The
    /// client itself gives up on a silent server after 90 s (noDataTimeout, UdpLibrary profile in FUN_142b5d5f0;
    /// DisconnectReasonTimeout).
    /// </para>
    /// <para>
    /// Measured on the wall clock (not summed tick deltas), so a slow server tick can't add silence the client never had.
    /// </para>
    /// </summary>
    public class SocketHeartbeat : IUpdate
    {
        /// <summary>The live client's SOE keep-alive interval (keepAliveDelay, FUN_143a0a980): the longest a healthy client is silent.</summary>
        public const double ClientKeepAliveSeconds = 29d;

        /// <summary>
        /// Default for the processes that serve game clients (the world's in-process gateway, the separate gateway):
        /// two keep-alive intervals (58 s) and a little more, so one lost keep-alive (UDP, unreliable) during a stretch
        /// where nothing else flows does not drop a healthy client; well under the client's own 90 s.
        /// </summary>
        public const double DefaultClientTimeoutSeconds = 60d;

        private static double defaultTimeoutSeconds = 180d;

        /// <summary>
        /// The silence timeout of every connection in this process that has none of its own, read at each check (a change
        /// counts at once). 180 s unless the process sets it: the world and the gateway set it from their configuration
        /// (CLIENT_TIMEOUT_SECONDS); the login server keeps 180. Values below the client's keep-alive interval are raised to
        /// it (a healthy idle client would be dropped every 29 s); 0 or less = never time out.
        /// </summary>
        public static double DefaultTimeoutSeconds
        {
            get => Volatile.Read(ref defaultTimeoutSeconds);
            set => Volatile.Write(ref defaultTimeoutSeconds, Sanitise(value));
        }

        /// <summary>A timeout as used: 0 or less = never; otherwise at least one client keep-alive interval plus a second.</summary>
        public static double Sanitise(double seconds)
            => seconds <= 0d || double.IsNaN(seconds) ? 0d : Math.Max(seconds, ClientKeepAliveSeconds + 1d);

        private static readonly Func<double> WallClock = () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        private readonly Func<double> clock;
        private readonly double? ownTimeout;
        private long lastBeatBits;

        public SocketHeartbeat() : this(null, null)
        {
        }

        /// <summary>
        /// <paramref name="clock"/>: seconds on a monotonic clock (tests pass a fake one); <paramref name="timeoutSeconds"/>:
        /// this connection's own timeout instead of <see cref="DefaultTimeoutSeconds"/>.
        /// </summary>
        public SocketHeartbeat(Func<double> clock, double? timeoutSeconds = null)
        {
            this.clock = clock ?? WallClock;
            ownTimeout = timeoutSeconds.HasValue ? Sanitise(timeoutSeconds.Value) : null;
            OnHeartbeat();
        }

        /// <summary>Seconds of silence after which the connection is dropped (0 = never).</summary>
        public double TimeoutSeconds => ownTimeout ?? DefaultTimeoutSeconds;

        /// <summary>Seconds since the client last sent anything.</summary>
        public double SilentSeconds => Math.Max(0d, clock() - BitConverter.Int64BitsToDouble(Interlocked.Read(ref lastBeatBits)));

        /// <summary>The client has been silent for the whole timeout.</summary>
        public bool Flatline
        {
            get
            {
                double timeout = TimeoutSeconds;
                return timeout > 0d && SilentSeconds >= timeout;
            }
        }

        /// <summary>The client sent something (receive thread).</summary>
        public void OnHeartbeat() => Interlocked.Exchange(ref lastBeatBits, BitConverter.DoubleToInt64Bits(clock()));

        /// <summary>Nothing to do per tick: silence is measured on the clock when asked.</summary>
        public void Update(double lastTick)
        {
        }
    }
}
