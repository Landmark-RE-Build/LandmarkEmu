using LandmarkEmulator.Shared.Game.Events;
using NLog;
using System;
using System.Collections.Generic;
using System.Net;

namespace LandmarkEmulator.Shared.Network
{
    public abstract class NetworkSession : IUpdate
    {
        protected static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Determines if queued packets can be processed.
        /// </summary>
        public bool CanProcessPackets => canProcessPackets && (WasConnected && !Disconnected);
        private bool canProcessPackets = false;
        protected bool WasConnected = false;

        public bool Disconnected { get; private set; }

        /// <summary>Server-assigned identity for edge↔zone routing (independent of UDP endpoint).</summary>
        public Guid BackendSessionId { get; private set; }

        public SocketHeartbeat Heartbeat { get; protected set; } = new SocketHeartbeat();

        /// <summary>
        /// The connection ended because the client went silent (<see cref="SocketHeartbeat.Flatline"/>) or stopped
        /// acknowledging (GameSession's unacknowledged timeout), not by a logout or a close from either side.
        /// </summary>
        public bool TimedOut { get; private set; }

        /// <summary>
        /// The client has gone silent for its timeout: marked <see cref="TimedOut"/> and disconnected (owners hear of it in
        /// <see cref="OnDisconnect"/> / <see cref="OnRemoved"/> like any other close). Tick thread.
        /// </summary>
        public void OnTimedOut()
        {
            if (Disconnected)
                return;
            TimedOut = true;
            log.Info($"Client {endPointRef} silent for {Heartbeat.SilentSeconds:0} s (timeout {Heartbeat.TimeoutSeconds:0} s); disconnecting.");
            OnTimedOutDisconnect();
        }

        /// <summary>How a silent connection is closed (GameSession: a Disconnect saying Timeout).</summary>
        public virtual void OnTimedOutDisconnect() => OnDisconnect();

        /// <summary>Marks the close as a timeout without the silence check (GameSession's unacknowledged timeout).</summary>
        protected void MarkTimedOut() => TimedOut = true;
        public EndPoint Endpoint { get; private set; }
        private string endPointRef;

        internal static event Action<NetworkSession, Guid> BackendSessionIdAssigned;

        protected void AssignBackendSessionId(Guid id)
        {
            if (BackendSessionId != Guid.Empty)
                return;

            BackendSessionId = id;
            BackendSessionIdAssigned?.Invoke(this, id);
        }

        private readonly object _eventLock = new();
        private readonly Queue<IEvent> _events = new();

        /// <summary>Queues an async/tick event for <see cref="Update"/> (thread-safe).</summary>
        public void EnqueueEvent(IEvent gameEvent)
        {
            if (gameEvent == null)
                return;

            lock (_eventLock)
                _events.Enqueue(gameEvent);
        }

        public delegate void SendEvent(EndPoint endpoint, byte[] message);

        /// <summary>
        /// Raised on <see cref="NetworkSession"/> creation for a new client.
        /// </summary>
        public event SendEvent OnSend;

        public virtual void OnAccept(EndPoint ep)
        {
            Endpoint = ep;
            endPointRef = ep.ToString();

            log.Debug($"Client connected: {ep}");
        }

        /// <summary>
        /// Invoked each server tick with the delta since the previous tick occurred.
        /// </summary>
        public virtual void Update(double lastTick)
        {
            Heartbeat.Update(lastTick);
            if (Heartbeat.Flatline)
                OnTimedOut();

            lock (_eventLock)
            {
                while (_events.TryPeek(out IEvent @event))
                {
                    if (@event == null)
                    {
                        _events.Dequeue();
                        continue;
                    }

                    if (!@event.CanExecute())
                        break;

                    _events.Dequeue();

                    try
                    {
                        @event.Execute();
                    }
                    catch (Exception exception)
                    {
                        log.Error(exception);
                    }
                }
            }
        }

        protected void SendRaw(byte[] data)
        {
            try
            {
                OnSend(Endpoint, data);
            }
            catch (Exception ex)
            {
                log.Error($"{ex}");
                OnDisconnect();
            }
        }

        public virtual void OnData(byte[] data)
        {
        }

        public virtual void OnDisconnect()
        {
            if (Disconnected)
                return;

            Disconnected = true;

            log.Debug($"Client disconnected: {endPointRef}");
        }

        /// <summary>
        /// The network manager dropped this session from its table (it disconnected or timed out). Runs on the thread
        /// that updates the sessions (the server tick), once, after <see cref="OnDisconnect"/> — which itself may run on
        /// the UDP receive thread (the client's own SOE Disconnect) — so owners can clean up on the tick.
        /// </summary>
        public virtual void OnRemoved()
        {
            // deliberately empty
        }

        public void TogglePacketProcessing(bool allowed)
        {
            if (!WasConnected && allowed)
                WasConnected = true;

            canProcessPackets = allowed;
        }
    }
}
