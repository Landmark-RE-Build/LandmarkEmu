using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// The holographic ore-vein overlay (the client's "MaterialVeinEffect": vein shapes drawn through the terrain with
    /// distance markers), used by the NovaTech Holoseeker and the prospecting gadgets. Verified in the client
    /// (2026-10-11).
    ///
    /// TerrainCell 0xEC, sub as a u32 (the body starts with its 2 high bytes):
    /// - 0x10 DetectVeins (reader FUN_14309bd90, handler FUN_1430ac8c0 → FUN_143605060 per vein): u32 n × vein
    ///   {u32 ResourceVeinGroupVeins id, f64 ×3 from, f64 ×3 to (global dm), u32}, f32 range (the effect keeps
    ///   range², must be &gt; 0), f32 fade, f32 duration, u32 n × u32 material filter (empty = all), u32 overlay id.
    ///   The client resolves the id (FUN_143f18e30) to its ResourceVeins entry and draws one effect per material with
    ///   a share &gt; 0, coloured by the material; an effect already shown at the same spot isn't added twice.
    /// - 0x11 EndDetectVeins: u32 overlay id → removes that overlay's effects (FUN_143606970).
    /// Each vein near the player (<see cref="OreVeins"/>) is sent as segments along its points, with its
    /// ResourceVeinGroupVeins id (the v2 surface and cave groups).
    /// </summary>
    public static class VeinOverlay
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>Overlay ids: one per source, so each can be cleared on its own.</summary>
        public const uint Holoseeker = 1, Prospecting = 2;

        private const float DrawRange = 64f, FadeTime = 2f, Duration = 30f;
        /// <summary>Vein points per drawn segment (a vein is 6-55 points, one voxel apart).</summary>
        private const int SegmentPoints = 5;

        /// <summary>
        /// Shows the veins within <paramref name="radius"/> metres of the player (only the ore items
        /// <paramref name="which"/> accepts, when given). One packet; cells already shown are skipped by the client.
        /// Returns how many vein cells were sent.
        /// </summary>
        public static int Send(WorldSession session, uint overlay, float radius, Func<uint, bool> which = null)
        {
            var cells = new List<(uint Id, (double, double, double) From, (double, double, double) To)>();
            foreach ((OreVeins.Vein vein, _, _) in EnvironmentServer.Instance.VeinsNear(session.Position, radius, session.ZoneId))
                if (which == null || which(vein.Item))
                    for (int i = 0; i + 1 < vein.Points.Count; i += SegmentPoints)
                    {
                        var a = vein.Points[i];
                        var b = vein.Points[Math.Min(i + SegmentPoints, vein.Points.Count - 1)];
                        cells.Add((vein.GroupVeinId, (a.X, a.Y, a.Z), (b.X, b.Y, b.Z)));
                    }
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.WriteBytes(new byte[2]);   // the TerrainCell sub-opcode is a u32 (the opcode supplies 2 bytes)
            w.Write((uint)cells.Count);
            foreach ((uint id, var from, var to) in cells)
            {
                w.Write(id);
                w.Write(from.Item1); w.Write(from.Item2); w.Write(from.Item3);
                w.Write(to.Item1); w.Write(to.Item2); w.Write(to.Item3);
                w.Write(0u);
            }
            w.Write(DrawRange);
            w.Write(FadeTime);
            w.Write(Duration);
            w.Write(0u);          // no material filter
            w.Write(overlay);
            session.EnqueueMessage(ZoneMessageOpcode.TerrainCellDetectVeins, output.ToArray());
            log.Debug($"Vein overlay {overlay} for {session.CharacterName}: {cells.Count} vein segment(s) within {radius} m.");
            return cells.Count;
        }

        /// <summary>Removes one overlay's vein effects (EndDetectVeins).</summary>
        public static void End(WorldSession session, uint overlay)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.WriteBytes(new byte[2]);
            w.Write(overlay);
            session.EnqueueMessage(ZoneMessageOpcode.TerrainCellEndDetectVeins, output.ToArray());
        }
    }
}
