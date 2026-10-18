using System.Collections.Generic;
using System.Linq;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;

namespace LandmarkEmulator.WorldServer.Network.Handlers
{
    /// <summary>
    /// Terrain cell requests (TerrainCell 0xEC). See docs/terrain.md for the formats.
    /// </summary>
    public class TerrainHandler
    {
        protected static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>The cells and their versions come from the Environment Server; this handler decides the replies.</summary>
        private static Game.Terrain.IEnvironmentServer Env => Game.Terrain.EnvironmentServer.Instance;

        /// <summary>
        /// Requested cells the server has changed are sent from the terrain store; the rest are reported up to date so
        /// the client keeps its generated terrain. Leaving requests unanswered makes the client stall until they time out.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.TerrainCellClientCellRequest)]
        public static void HandleClientCellRequest(WorldSession session, TerrainCellClientCellRequest request)
        {
            Answer(session, request.Cells.Select(c => c.CellId).ToList(),
                request.Cells.ToDictionary(c => c.CellId, c => new[] { c.LayerVersions[0], c.LayerVersions[1], c.LayerVersions[2] }));
        }

        /// <summary>
        /// A request the client gave up on after 3 seconds (TerrainCell 2): answered again, so the cell isn't left
        /// on generated terrain when the first answer was slow or lost.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.TerrainCellPendingRequestTimeout)]
        public static void HandlePendingRequestTimeout(WorldSession session, TerrainCellPendingRequestTimeout request)
        {
            // Usually the answer is still queued behind other cell data (a login or a long flight asks for hundreds
            // of edited cells, ~12 KB each, and the client gives up after 3 s). Sending it again doubled the traffic,
            // more requests timed out, building stalled (the client refuses edits while a fetch is pending) and
            // the original ground showed until the data landed (2026-10-13). The client applies late cell data, so
            // a cell sent recently at the same version is left to arrive.
            uint version = Env.CurrentVersion(request.CellId, session.ZoneId);
            lock (sentCells)
                if (sentCells.TryGetValue(session, out var sent) && sent.TryGetValue(request.CellId, out var last)
                    && last.Version == version && System.Environment.TickCount64 - last.At < ResendAfterMs)
                    return;
            log.Debug($"{session.CharacterName}: cell request for 0x{request.CellId:X} timed out on the client; answering again.");
            Answer(session, new List<ulong> { request.CellId });
        }

        /// <summary>Edited cells sent to each player recently: cell → (version, Environment.TickCount64).</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorldSession, Dictionary<ulong, (uint Version, long At)>> sentCells = new();
        private const long ResendAfterMs = 20000;

        /// <param name="held">Per cell, the layer versions the client already holds (ClientCellRequest: 4 × u32 per
        /// cell, the versions of its own copy, FUN_143f56980); null for a re-answer after a timeout.</param>
        private static void Answer(WorldSession session, List<ulong> cells, Dictionary<ulong, uint[]> held = null)
        {
            var stored = new List<ulong>();
            uint zone = session.ZoneId;
            bool home = session.OnHomeContinent;   // the captured store holds home-continent cells only
            var reply = new TerrainCellUpToDateCells();
            // First the decisions (version checks only), then the server's bodies in one fetch — one round trip to a
            // remote Environment Server for the whole request (in-process: the same calls as before).
            var send = new List<(ulong Cell, bool Always)>();
            foreach (ulong cellId in cells)
            {
                uint[] mine = held != null && held.TryGetValue(cellId, out uint[] h) ? h : null;
                bool server = Env.Has(cellId, zone);
                if (mine != null && (server || mine.Any(v => v != 0)))
                {
                    // The client's copy is current in every layer: "up to date", nothing to send (live's answer).
                    // A layer the client holds but the server doesn't (a stale cached copy) is sent back empty.
                    bool current = true;
                    for (uint layer = 0; layer < Game.Terrain.TerrainEdits.LayerCount && current; layer++)
                        current = mine[layer] == Env.LayerVersion(cellId, zone, layer);
                    if (current)
                    {
                        reply.CellIds.Add(cellId);
                        continue;
                    }
                    Env.SampleVersion(cellId, zone, mine);
                    if (!server && !(home && Env.HasCapturedCell(cellId)))
                    {
                        send.Add((cellId, true));
                        continue;
                    }
                }
                if (server)
                    send.Add((cellId, false));
                else if (home && Env.HasCapturedCell(cellId))
                    stored.Add(cellId);
                else
                    reply.CellIds.Add(cellId);
            }
            if (send.Count > 0)
            {
                List<ulong> plain = send.Where(s => !s.Always).Select(s => s.Cell).ToList(),
                    empty = send.Where(s => s.Always).Select(s => s.Cell).ToList();
                var bodies = new Dictionary<ulong, byte[]>();
                IReadOnlyList<byte[]> fetched = Env.BuildCellsUpdates(plain, zone);
                for (int i = 0; i < plain.Count; i++)
                    bodies[plain[i]] = fetched[i];
                fetched = Env.BuildCellsUpdates(empty, zone, always: true);
                for (int i = 0; i < empty.Count; i++)
                    bodies[empty[i]] = fetched[i];
                foreach ((ulong cellId, bool always) in send)
                {
                    byte[] body = bodies.GetValueOrDefault(cellId);
                    if (body == null)
                    {
                        // A remote Environment Server away and the body not cached (terrain-service.md §6): "up to date"
                        // now — the client keeps what it has instead of stalling 6 s and blocking its build tool — and a
                        // manifest note when the service is back, so it fetches the cell then.
                        reply.CellIds.Add(cellId);
                        if (!always && !Env.Available)
                            Game.Terrain.ZoneTerrain.OweRefetch(session, cellId);
                        continue;
                    }
                    session.EnqueueMessage(ZoneMessageOpcode.TerrainCellCellsUpdate, body);
                    if (always)
                        continue;
                    uint version = Env.CurrentVersion(cellId, zone);
                    lock (sentCells)
                        sentCells.GetOrCreateValue(session)[cellId] = (version, System.Environment.TickCount64);
                }
            }

            log.Debug($"Terrain cell request for {cells.Count} cell(s): {stored.Count} from the store.");
            if (stored.Count > 0)
                session.EnqueueMessage(ZoneMessageOpcode.TerrainCellCellsUpdate, Env.BuildCapturedCellsUpdate(stored));
            if (reply.CellIds.Count > 0)
                session.EnqueueMessage(reply);
        }
    }
}
