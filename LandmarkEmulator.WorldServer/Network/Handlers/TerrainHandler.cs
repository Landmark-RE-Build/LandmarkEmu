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
            Answer(session, request.Cells.Select(c => c.CellId).ToList());
        }

        /// <summary>
        /// A request the client gave up on after 3 seconds (TerrainCell 2): answered again, so the cell isn't left
        /// on generated terrain when the first answer was slow or lost.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.TerrainCellPendingRequestTimeout)]
        public static void HandlePendingRequestTimeout(WorldSession session, TerrainCellPendingRequestTimeout request)
        {
            log.Debug($"{session.CharacterName}: cell request for 0x{request.CellId:X} timed out on the client; answering again.");
            Answer(session, new List<ulong> { request.CellId });
        }

        private static void Answer(WorldSession session, List<ulong> cells)
        {
            var stored = new List<ulong>();
            uint zone = session.ZoneId;
            bool home = session.OnHomeContinent;   // the captured store holds home-continent cells only
            var reply = new TerrainCellUpToDateCells();
            foreach (ulong cellId in cells)
            {
                if (Game.Terrain.TerrainEdits.Has(cellId, zone))
                    session.EnqueueMessage(ZoneMessageOpcode.TerrainCellCellsUpdate, Game.Terrain.TerrainEdits.BuildUpdate(cellId, zone));
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
