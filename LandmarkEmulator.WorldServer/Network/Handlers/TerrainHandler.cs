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

        /// <summary>
        /// Requested cells the server has changed are sent from the terrain store; the rest are reported up to date so
        /// the client keeps its generated terrain. Leaving requests unanswered makes the client stall until they time out.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.TerrainCellClientCellRequest)]
        public static void HandleClientCellRequest(WorldSession session, TerrainCellClientCellRequest request)
        {
            Answer(session, request.Cells.Select(c => c.CellId).ToList(),
                request.Cells.ToDictionary(c => c.CellId, c => new[] { (uint)c.Unknown1, (uint)(c.Unknown1 >> 32), (uint)c.Unknown2 }));
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
            uint version = Game.Terrain.TerrainEdits.CurrentVersion(request.CellId, session.ZoneId);
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
            foreach (ulong cellId in cells)
            {
                uint[] mine = held != null && held.TryGetValue(cellId, out uint[] h) ? h : null;
                bool server = Game.Terrain.TerrainEdits.Has(cellId, zone);
                if (mine != null && (server || mine.Any(v => v != 0)))
                {
                    // The client's copy is current in every layer: "up to date", nothing to send (live's answer).
                    // A layer the client holds but the server doesn't (a stale cached copy) is sent back empty.
                    bool current = true;
                    for (uint layer = 0; layer < Game.Terrain.TerrainEdits.LayerCount && current; layer++)
                        current = mine[layer] == Game.Terrain.TerrainEdits.LayerVersion(cellId, zone, layer);
                    if (current)
                    {
                        reply.CellIds.Add(cellId);
                        continue;
                    }
                    Game.Terrain.TerrainEdits.SampleVersion(cellId, zone, mine);
                    if (!server && !(home && Game.Terrain.TerrainStore.Get(cellId) != null))
                    {
                        session.EnqueueMessage(ZoneMessageOpcode.TerrainCellCellsUpdate,
                            Game.Terrain.TerrainEdits.BuildUpdate(cellId, zone, always: true));
                        continue;
                    }
                }
                if (server)
                {
                    session.EnqueueMessage(ZoneMessageOpcode.TerrainCellCellsUpdate, Game.Terrain.TerrainEdits.BuildUpdate(cellId, zone));
                    uint version = Game.Terrain.TerrainEdits.CurrentVersion(cellId, zone);
                    lock (sentCells)
                        sentCells.GetOrCreateValue(session)[cellId] = (version, System.Environment.TickCount64);
                }
                else if (home && Game.Terrain.TerrainStore.Get(cellId) != null)
                    stored.Add(cellId);
                else
                    reply.CellIds.Add(cellId);
            }

            log.Debug($"Terrain cell request for {cells.Count} cell(s): {stored.Count} from the store.");
            if (stored.Count > 0)
                session.EnqueueMessage(ZoneMessageOpcode.TerrainCellCellsUpdate, Game.Terrain.TerrainStore.BuildCellsUpdate(stored));
            if (reply.CellIds.Count > 0)
                session.EnqueueMessage(reply);
        }
    }
}
