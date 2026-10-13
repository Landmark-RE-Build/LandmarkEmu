using LandmarkEmulator.Shared.Configuration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace LandmarkEmulator.Shared.Network
{
    public class NetworkManager<T> : Singleton<NetworkManager<T>> where T : NetworkSession, new()
    {
        private Connection connection;

        private readonly ConcurrentQueue<T> pendingAdd = new();
        private readonly ConcurrentQueue<T> pendingRemove = new();

        private readonly Dictionary<EndPoint, T> sessions = new();
        private readonly Dictionary<Guid, T> sessionsById = new();
        /// <summary>
        /// Guards both tables: the receive thread adds sessions while the tick thread walks and prunes them (two
        /// clients connecting at once used to crash the server with "Collection was modified").
        /// </summary>
        private readonly object sync = new();

        /// <summary>
        /// Initialises a new <see cref="NetworkManager{T}"/>.
        /// </summary>
        public void Initialise(NetworkConfig config)
        {
            NetworkSession.BackendSessionIdAssigned += OnBackendSessionIdAssigned;

            connection = new Connection(IPAddress.Parse(config.Host), config.Port);
            connection.OnMessage += (remoteEP, message) =>
            {
                // Existing session, pass data for processing
                T session;
                lock (sync)
                    sessions.TryGetValue(remoteEP, out session);
                if (session != null)
                    session.OnData(message);
                else
                {
                    var newSession = new T();
                    newSession.OnAccept(remoteEP);

                    // Add new Session, setup Event Handler, and send received data
                    lock (sync)
                        sessions[newSession.Endpoint] = newSession;
                    newSession.OnSend += (endpoint, data) =>
                    {
                        connection.SendBytes(endpoint, data);
                    };
                    newSession.TogglePacketProcessing(true);
                    newSession.OnData(message);
                }
            };
        }

        private void OnBackendSessionIdAssigned(NetworkSession session, Guid id)
        {
            lock (sync)
                if (session is T typed && !sessionsById.ContainsKey(id))
                    sessionsById[id] = typed;
        }

        /// <summary>
        /// Returns <see cref="{T}"/> that matches the predicate. Must be a Single match, or error.
        /// </summary>
        public T GetSession(Func<T, bool> func)
        {
            lock (sync)
                return sessions.Values.SingleOrDefault(func);
        }

        /// <summary>A snapshot of the connected sessions.</summary>
        public List<T> Sessions()
        {
            lock (sync)
                return sessions.Values.ToList();
        }

        /// <summary>Returns the session for a backend <see cref="NetworkSession.BackendSessionId"/>, if connected.</summary>
        public T GetSessionById(Guid sessionId)
        {
            lock (sync)
            {
                sessionsById.TryGetValue(sessionId, out T session);
                return session;
            }
        }

        /// <summary>
        /// Shutdown this <see cref="NetworkManager"/>, releasing any connections and ending all sessions.
        /// </summary>
        public void Shutdown()
        {
            NetworkSession.BackendSessionIdAssigned -= OnBackendSessionIdAssigned;

            List<T> all;
            lock (sync)
                all = sessions.Values.ToList();
            foreach (T session in all)
                session.OnDisconnect();

            connection?.Shutdown();
        }

        /// <summary>
        /// Invoked each server tick with the delta since the previous tick occurred.
        /// </summary>
        public void Update(double lastTick)
        {

            // Operate on existing sessions (a snapshot: new ones may arrive on the receive thread meanwhile).
            List<T> current;
            lock (sync)
                current = sessions.Values.ToList();
            foreach (T session in current)
            {
                if (session.Disconnected || session.Heartbeat.Flatline)
                    pendingRemove.Enqueue(session);
                else
                    session.Update(lastTick);
            }

            // Remove any sessions that are pending removal.
            while (pendingRemove.TryDequeue(out T session))
                lock (sync)
                {
                    sessions.Remove(session.Endpoint);
                    if (session.BackendSessionId != Guid.Empty)
                        sessionsById.Remove(session.BackendSessionId);
                }
        }

        /// <summary>
        /// Using the <see cref="Connection"/> for this <see cref="NetworkManager{T}"/>, send data to the given <see cref="IPEndPoint"/>.
        /// </summary>
        public void SendData(IPEndPoint ep, byte[] data)
        {
            connection.SendBytes(ep, data);
        }
    }
}
